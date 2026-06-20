namespace BG2VR.VrInput
{
    /// <summary>
    /// ボタン押下を「短押し（閾値未満でリリースした瞬間）/ 長押し（閾値到達の瞬間・1 回）」に分類する
    /// （System のみ依存・純ロジック）。長押し発火後のリリースでは何も出さない。
    /// ConsumePress() は現在の押下をリリースまで分類対象外にする（world UI 配置確定に使った押下が
    /// 戻る/再センターに化けるのを防ぐ・spec §4-5）。
    /// </summary>
    public sealed class HoldButtonClassifier
    {
        public const float LongPressSeconds = 0.6f;

        public struct Result
        {
            public bool ShortPress;  // 短押し確定（リリースフレーム）
            public bool LongPress;   // 長押し確定（閾値到達フレーム・押下継続中）
            public bool JustPressed; // 押下開始フレーム（配置確定用の生エッジ）
        }

        private bool m_prev;
        private float m_heldTime;
        private bool m_longFired;
        private bool m_consumed;

        public Result Update(bool pressed, float dt)
        {
            var r = new Result();
            if (pressed && !m_prev)
            {
                r.JustPressed = true;
                m_heldTime = 0f;
                m_longFired = false;
                m_consumed = false;
            }
            else if (pressed)
            {
                m_heldTime += dt;
                if (!m_longFired && !m_consumed && m_heldTime >= LongPressSeconds)
                {
                    m_longFired = true;
                    r.LongPress = true;
                }
            }
            else if (m_prev) // リリース
            {
                if (!m_longFired && !m_consumed) r.ShortPress = true;
            }
            m_prev = pressed;
            return r;
        }

        /// <summary>現在の押下を消費する（リリースまで Short/Long を発火しない）。未押下時は no-op。</summary>
        public void ConsumePress()
        {
            if (m_prev) m_consumed = true;
        }

        public void Reset()
        {
            m_prev = false;
            m_heldTime = 0f;
            m_longFired = false;
            m_consumed = false;
        }
    }
}
