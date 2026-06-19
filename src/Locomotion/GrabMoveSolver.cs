using UnityEngine;

namespace BG2VR.Locomotion
{
    /// <summary>
    /// grip 移動の数学（純関数・xUnit 可）。「掴んだ空間は手と一緒に動く」拘束を
    /// 「前フレームの手の world 位置を固定」する rig pose として直接解くことで、
    /// 平行移動と手の位置を中心とした yaw 回転が 1 本の式で同時に成立する。
    /// 重要: Quaternion.Euler/AngleAxis/LookRotation/Inverse は native ECall でテストホスト不在のため
    /// 使わない（Phase3 実証）。q*q / q*v 演算子（managed）と Mathf のみで書く。
    /// spec: docs/superpowers/specs/2026-06-05-bg2-vr-grip-move-locomotion-design.md §3 §5
    /// </summary>
    public static class GrabMoveSolver
    {
        // 手が真上/真下向きで forward の水平成分がこの二乗長未満なら yaw delta を 0 にする（特異点ガード）。
        public const float YawSingularityEpsSq = 1e-4f;

        public struct RigPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        /// <summary>
        /// 1 フレーム分の grip 移動を解く。rigScale = rig.localScale.x（= 1/WorldScale。
        /// snapshot の pose は tracking 空間メートルのため world 換算に必要）。
        /// 縦回転（pitch/roll）は yaw 抽出の時点で構造的に落ちる（酔い対策）。
        /// 契約: prevLocal*/currLocal* は「同一の手」の「連続フレーム」の pose であること。
        /// 手の切替・engage 直後は呼び出し側（GrabMoveRunner）が marker を再同期し本関数を skip する
        ///（守らないと巨大差分を 1 フレームで適用してしまう）。
        /// </summary>
        public static RigPose Step(
            Vector3 rigPos, Quaternion rigRot, float rigScale,
            Vector3 prevLocalPos, Quaternion prevLocalRot,
            Vector3 currLocalPos, Quaternion currLocalRot)
        {
            Vector3 handPrevWorld = rigPos + rigRot * (prevLocalPos * rigScale);

            // 回転 delta の world-Y twist 成分（swing-twist 分解）。旧実装の「forward 水平投影の
            // 方位角差」はコントローラのピッチ構えで 1/cos(pitch) に増幅され「回転しすぎ」になった
            //（40° 構えで約 1.3 倍）。twist は増幅せず、euler-y 抽出とも小角で一致。
            float deltaYaw = TwistYawOf(currLocalRot * Conjugate(prevLocalRot));

            // 「掴んだ空間は手と一緒に動く」＝rig には手の yaw 変化の逆を与える。
            Quaternion newRot = NormalizeYaw(YawRotation(-deltaYaw) * rigRot);
            // 手の world 位置を固定する位置（回転中心=手 が自動的に成立）。
            Vector3 newPos = handPrevWorld - newRot * (currLocalPos * rigScale);
            return new RigPose { Position = newPos, Rotation = newRot };
        }

        /// <summary>
        /// 今フレームの rig pose 変化（world 剛体変換 D）を累積 A に左から合成する（リセット用）。
        /// 初期 pose 保存方式にしない理由: fork の目線高さ（F10）は rig position を差分加算で動かすため、
        /// pose snap 復元だと grip 後の目線高さ変更まで巻き戻し fork 側の差分管理と永続的にズレる。
        /// 垂直移動と yaw 回転は可換なので A⁻¹ 方式は目線高さ変更を保存したまま正確（spec §5）。
        /// </summary>
        public static void AccumulateDelta(
            Vector3 oldPos, Quaternion oldRot, Vector3 newPos, Quaternion newRot,
            ref Vector3 accumPos, ref Quaternion accumRot)
        {
            Quaternion rd = NormalizeYaw(newRot * Conjugate(oldRot)); // rig 回転は常に yaw-only（spec §2）
            Vector3 td = newPos - rd * oldPos;
            accumRot = NormalizeYaw(rd * accumRot);
            accumPos = rd * accumPos + td;
        }

        /// <summary>累積 A の逆を rig pose に適用する（grip 移動分だけ巻き戻す）。</summary>
        public static RigPose ApplyInverse(Vector3 accumPos, Quaternion accumRot, Vector3 rigPos, Quaternion rigRot)
        {
            Quaternion inv = Conjugate(accumRot); // 単位 quaternion の逆元 = 共役
            return new RigPose
            {
                Position = inv * (rigPos - accumPos),
                Rotation = NormalizeYaw(inv * rigRot),
            };
        }

        /// <summary>
        /// 回転 delta の world Y 軸まわり twist 成分(rad)を返す（swing-twist 分解・±π wrap）。
        /// q と −q（同一回転の符号反転表現）で同じ値になる。縦回転（swing 成分）は構造的に落ちる。
        /// 純粋な水平軸 180° 回転（y=w=0）はフレーム間 delta では実質起きないため 0 を返す。
        /// </summary>
        public static float TwistYawOf(Quaternion delta)
        {
            float m = Mathf.Sqrt(delta.y * delta.y + delta.w * delta.w);
            if (m < 1e-9f) return 0f;
            return WrapPi(2f * Mathf.Atan2(delta.y / m, delta.w / m));
        }

        /// <summary>forward の水平射影から yaw(rad) を抽出（テスト検証・yaw-only quat の角度取得用）。
        /// 真上/真下向き（水平成分が退化）は false。</summary>
        public static bool TryYawOf(Quaternion q, out float yawRad)
            => TryYawOfForward(q * Vector3.forward, out yawRad);

        /// <summary>forward ベクトル版（TryYawOf の実体）。</summary>
        public static bool TryYawOfForward(Vector3 f, out float yawRad)
        {
            float horizSq = f.x * f.x + f.z * f.z;
            if (horizSq < YawSingularityEpsSq)
            {
                yawRad = 0f;
                return false;
            }
            yawRad = Mathf.Atan2(f.x, f.z);
            return true;
        }

        /// <summary>world Y 軸回転 quaternion の成分直構築（Quaternion.AngleAxis の ECall 回避）。</summary>
        public static Quaternion YawRotation(float radians)
        {
            float h = radians * 0.5f;
            return new Quaternion(0f, Mathf.Sin(h), 0f, Mathf.Cos(h));
        }

        /// <summary>±π へ正規化（180° またぎの yaw 差分を短い方の弧にする）。</summary>
        public static float WrapPi(float rad)
        {
            while (rad > Mathf.PI) rad -= 2f * Mathf.PI;
            while (rad < -Mathf.PI) rad += 2f * Mathf.PI;
            return rad;
        }

        // 単位 quaternion の共役（= 逆元）。Quaternion.Inverse の ECall 回避。
        private static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        // yaw-only quaternion (0, y, 0, w) へ数値健全化。x/z は不変条件（rig は常に yaw-only）として
        // 0 に丸め、長時間 grip での浮動小数ドリフトによる傾き混入を防ぐ。
        private static Quaternion NormalizeYaw(Quaternion q)
        {
            float m = Mathf.Sqrt(q.y * q.y + q.w * q.w);
            if (m < 1e-9f) return new Quaternion(0f, 0f, 0f, 1f);
            return new Quaternion(0f, q.y / m, 0f, q.w / m);
        }
    }
}
