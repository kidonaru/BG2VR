using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// UI パネル移動ドラッグの位置数学（純関数・xUnit 可）。全て rig-local 空間
    /// （snapshot の pose と root の localPosition/localRotation は同じ rig-local 単位＝スケール換算不要。
    /// rig がどう動いても追従関係は不変＝同時 locomotion と構造的に干渉しない）。
    /// 移動は「engage 時の手→パネル相対**位置**の保持」（ToFrame で捕捉・回転 out は呼出元が破棄）。
    /// 向きは PlacementSolver.ComputeRotation の自動制御＝本クラスは関与しない（spec §3）。
    /// FromFrame は ToFrame の逆関数（現在 prod 呼出なし・テスト済みの対として維持）。
    /// Quaternion.LookRotation/AngleAxis/Inverse は native ECall でテストホスト不在のため使わない
    ///（q*q / q*v 演算子 + 成分直構築。GrabMoveSolver で実証済みパターン）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-auto-orient-design.md §3
    ///（旧 grip 掴み spec: 2026-06-05-bg2-vr-ui-grip-grab-design.md §4 §5）
    /// </summary>
    public static class PanelGrabSolver
    {
        // 押し引きの距離 clamp と乗算レート（固定値・Config 化しない＝ユーザー固定値選好・spec §4）。
        public const float MinDistance = 0.5f;
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
        /// 押し引き（移動ドラッグ中のスティック上下）: 手→パネルの相対位置を中心線沿いに乗算で伸縮。
        /// 方向は spec §4 で固定: stickY > 0（上）= 伸びる（遠ざける）/ 下 = 縮む（引き寄せる）。
        /// 厳密なレーザー線沿いではないが、grab 点と中心のオフセットは距離に対し小さく体感差なし（spec §4）。
        /// 下限を min(現在値, MinDistance) にするのは、固定 0.5 clamp だと例えば 0.3m で捕捉した直後に
        /// 押した瞬間 0.5m へワープするため（引きはその場維持・押しは連続的に伸ばす）。
        /// </summary>
        public static Vector3 PushPull(Vector3 relPos, float stickY, float dt)
        {
            float mag = relPos.magnitude;
            if (mag < 1e-6f || stickY == 0f) return relPos;
            float lower = Mathf.Min(mag, MinDistance);
            float target = Mathf.Clamp(mag * Mathf.Exp(PushPullRate * stickY * dt), lower, MaxDistance);
            return relPos * (target / mag);
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
