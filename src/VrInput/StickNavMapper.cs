using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// 左スティック → 十字キー相当の 4 方向ナビ（UnityEngine.Vector2 + System のみ依存・純ロジック）。
    /// 軸ごとに engage しきい値で Held（レベル）、立ち上がりで Pulse（エッジ 1 回）を出す。
    /// release はヒステリシス（engage × ReleaseRatio）でチャタリングを防ぐ。
    /// 斜めは両方向同時 Held（実スティックの dpad composite binding と同挙動）。
    /// しきい値は引数渡し（Configs 非参照の純関数規約・呼び出し側が解決済み値を渡す）。
    /// </summary>
    public sealed class StickNavMapper
    {
        /// <summary>release しきい値 = engage × この比率（構造定数・チャタリング防止）。</summary>
        public const float ReleaseRatio = 0.8f;

        public struct NavState
        {
            public bool UpHeld, DownHeld, LeftHeld, RightHeld;
            public bool UpPulse, DownPulse, LeftPulse, RightPulse;
        }

        private bool m_up, m_down, m_left, m_right;

        public NavState Update(Vector2 stick, float engageThreshold)
        {
            var s = new NavState();
            s.UpPulse = UpdateDir(ref m_up, stick.y, engageThreshold);
            s.DownPulse = UpdateDir(ref m_down, -stick.y, engageThreshold);
            s.LeftPulse = UpdateDir(ref m_left, -stick.x, engageThreshold);
            s.RightPulse = UpdateDir(ref m_right, stick.x, engageThreshold);
            s.UpHeld = m_up;
            s.DownHeld = m_down;
            s.LeftHeld = m_left;
            s.RightHeld = m_right;
            return s;
        }

        private static bool UpdateDir(ref bool held, float value, float engage)
        {
            bool now = held ? value >= engage * ReleaseRatio : value >= engage;
            bool pulse = now && !held;
            held = now;
            return pulse;
        }

        public void Reset()
        {
            m_up = false;
            m_down = false;
            m_left = false;
            m_right = false;
        }
    }
}
