using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// UI パネル移動ドラッグの位置数学（純関数・xUnit 可）。全て rig-local 空間
    /// （snapshot の pose と root の localPosition/localRotation は同じ rig-local 単位＝スケール換算不要。
    /// rig がどう動いても追従関係は不変＝同時 locomotion と構造的に干渉しない）。
    /// 移動は eye-centered aim（AimCapture/AimResolve）: engage 時の「eye→パネル方向」を hand 回転フレームへ
    /// 捕捉し、傾け（hand 回転）でパネルを目中心シェル上に距離一定で動かす（円柱+上下ドーム）。push/pull は
    /// PushPullDistance で目距離だけ伸縮する。向きは PlacementSolver.ComputeRotation の自動制御＝本クラスは関与しない（spec §3）。
    /// ToFrame/FromFrame は旧 grip 掴み（手中心剛体追従）の対＝現在 prod 呼出なし・テスト済みの対として維持。
    /// Quaternion.LookRotation/AngleAxis/Inverse は native ECall でテストホスト不在のため使わない
    ///（q*q / q*v 演算子 + 成分直構築。GrabMoveSolver で実証済みパターン）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-auto-orient-design.md §3
    ///（旧 grip 掴み spec: 2026-06-05-bg2-vr-ui-grip-grab-design.md §4 §5）
    /// </summary>
    public static class PanelGrabSolver
    {
        // 押し引きの距離 clamp と乗算レート（固定値・Config 化しない＝ユーザー固定値選好・spec §4）。
        public const float MinDistance = 0.3f;
        public const float MaxDistance = 8f;
        public const float PushPullRate = 1.0f; // /s（distance *= e^(rate·stickY·dt)）

        public struct PanelPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        /// <summary>pose をフレーム（位置+回転）のローカル姿勢へ変換する。</summary>
        public static void ToFrame(Vector3 framePos, Quaternion frameRot, Vector3 pos, Quaternion rot,
            out Vector3 localPos, out Quaternion localRot)
        {
            Quaternion inv = Conjugate(frameRot);
            localPos = inv * (pos - framePos);
            localRot = Normalize(inv * rot);
        }

        /// <summary>フレームローカル姿勢を rig-local へ戻す（ToFrame の逆）。</summary>
        public static PanelPose FromFrame(Vector3 framePos, Quaternion frameRot, Vector3 localPos, Quaternion localRot)
            => new PanelPose
            {
                Position = framePos + frameRot * localPos,
                Rotation = Normalize(frameRot * localRot),
            };

        /// <summary>
        /// eye-centered aim 捕捉（移動 engage 時）: eye→パネルの「方向」を hand 回転フレームへ、距離はそのまま。
        /// コントローラを傾けると <see cref="AimResolve"/> がパネルを**目中心シェル上**で掃く（距離一定＝円柱+
        /// 上下ドーム）。push/pull は距離だけ変える。engage 時の方向は捕捉して保持＝レーザー命中点とパネル中心の
        /// オフセットがそのまま追従する（ジャンプなし）。dist が縮退（panelPos==eyePos・VR では起きない）なら
        /// hand 回転フレームの forward を返す（AimResolve で hand 前方になる）。
        /// </summary>
        public static void AimCapture(Vector3 panelPos, Vector3 eyePos, Quaternion handRot,
            out Vector3 localDir, out float dist)
        {
            Vector3 toPanel = panelPos - eyePos;
            dist = toPanel.magnitude;
            if (dist < 1e-6f) { localDir = new Vector3(0f, 0f, 1f); dist = 0f; return; }
            localDir = Conjugate(handRot) * (toPanel / dist); // hand 回転フレームでの eye→パネル単位方向
        }

        /// <summary>
        /// <see cref="AimCapture"/> の逆: eye + (handRot·localDir)·dist。handRot 変化（傾け）で localDir が
        /// 同じ回転だけ振られ、パネルは目中心シェル上を距離一定で移動する。同 handRot なら捕捉時の位置に一致。
        /// </summary>
        public static Vector3 AimResolve(Vector3 eyePos, Quaternion handRot, Vector3 localDir, float dist)
            => eyePos + (handRot * localDir) * dist;

        /// <summary>
        /// 押し引き（移動ドラッグ中スティック上下）: **目からの距離**を乗算で伸縮する。
        /// 方向は spec §4 で固定: stickY > 0（上）= 遠ざける / 下 = 引き寄せる。
        /// 下限を min(現在値, MinDistance) にするのは、固定 clamp だと近距離で捕捉直後に押した瞬間
        /// MinDistance へワープするため（引きはその場維持・押しは連続的に伸ばす）。
        /// </summary>
        public static float PushPullDistance(float dist, float stickY, float dt)
        {
            if (stickY == 0f || dist < 1e-6f) return dist;
            float lower = Mathf.Min(dist, MinDistance);
            return Mathf.Clamp(dist * Mathf.Exp(PushPullRate * stickY * dt), lower, MaxDistance);
        }

        // 単位 quaternion の共役（= 逆元）。Quaternion.Inverse の ECall 回避。
        private static Quaternion Conjugate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        // 成分正規化（長時間の合成での浮動小数ドリフト抑制）。退化時は identity。
        private static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-9f) return new Quaternion(0f, 0f, 0f, 1f);
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
    }
}
