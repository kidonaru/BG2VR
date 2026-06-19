using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// world UI パネルの配置数学（純関数）。配置の自由度は（水平距離・高さ・ヨー）の 3 スカラー
    ///＝config（WorldUiDistance/WorldUiVerticalOffset/WorldUiYaw）と 1:1 対応し、Encode/Decode で
    /// 相互変換する（spec §4・手動アンカー機構の廃止）。すべて rig 軸相対＝頭の向きに依存しない。
    /// 向きは ComputeRotation の自動制御: ヨー=常に視点を向く / ピッチ=仰角の ±θ0 超過分のみ
    ///（直立帯 + ドーム・C0 連続）/ ロール=0（spec §3）。
    /// Quaternion.Euler/LookRotation/AngleAxis は native ECall のため使わない
    ///（成分直構築 + q*q / q*v 演算子。PanelGrabSolver と同パターン）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-auto-orient-design.md §3 §4
    /// </summary>
    public static class PlacementSolver
    {
        // RT の論理解像度（16:9）。物理高さ算出に使う。
        public const float RefWidthPx = 1920f;
        public const float RefHeightPx = 1080f;

        /// <summary>配置 3 スカラー（config と 1:1）。</summary>
        public struct Placement
        {
            public float HorizDist; // 水平距離(m)
            public float Height;    // 視点からの高さ(m)
            public float YawDeg;    // 方位（rig +Z 基準・+x 側が正・度）
        }

        /// <summary>視点→パネルのオフセット（rig-local）を 3 スカラーへ encode。</summary>
        public static Placement Encode(Vector3 offset)
            => new Placement
            {
                HorizDist = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z),
                Height = offset.y,
                YawDeg = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg,
            };

        /// <summary>3 スカラーから視点→パネルのオフセットへ decode（Encode の逆）。</summary>
        public static Vector3 Decode(float horizDist, float height, float yawDeg)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad) * horizDist, height, Mathf.Cos(rad) * horizDist);
        }

        /// <summary>
        /// 自動向き: panel-local +Z（読み面 −Z の裏向き）が「方位=yaw・仰角=sign(ε)·max(0,|ε|−θ0)」の
        /// 単位ベクトルへ一致する回転を返す。Unity 左手系では +X 回転が +Z を下に倒すため、
        /// +Z を仰角 e だけ上げる回転は qPitch(−e)。
        /// </summary>
        public static Quaternion ComputeRotation(Vector3 offset, float uprightAngleDeg)
        {
            float horiz = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
            if (horiz < 1e-6f && Mathf.Abs(offset.y) < 1e-6f)
                return new Quaternion(0f, 0f, 0f, 1f); // 縮退（原点）: 無回転

            float yaw = Mathf.Atan2(offset.x, offset.z);
            float eps = Mathf.Atan2(offset.y, horiz);
            float t0 = Mathf.Max(0f, uprightAngleDeg) * Mathf.Deg2Rad;
            float elev = Mathf.Sign(eps) * Mathf.Max(0f, Mathf.Abs(eps) - t0);

            var qYaw = new Quaternion(0f, Mathf.Sin(yaw * 0.5f), 0f, Mathf.Cos(yaw * 0.5f));
            var qPitch = new Quaternion(Mathf.Sin(-elev * 0.5f), 0f, 0f, Mathf.Cos(-elev * 0.5f));
            return qYaw * qPitch;
        }
    }
}
