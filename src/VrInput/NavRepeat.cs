namespace BG2VR.VrInput
{
    /// <summary>
    /// held（レベル）入力を「初回即時 → delay 後に interval ごと連射」のパルス列へ変換する純ロジック
    /// （System のみ依存）。設定パネルのナビ handler（HandleKeyArrowUp 等）はエッジ専用で repeat を
    /// 持たないため、スティックを倒し続けたときの連続スクロール/スライダー増減をこれで作る。
    /// しきい値は引数渡し（Configs 非参照・呼び出し側が解決済み値を渡す既存規約）。
    /// </summary>
    public sealed class NavRepeat
    {
        private bool m_prevHeld;
        private float m_timer;
        private bool m_repeating; // 初動 delay を消化して連射フェーズに入ったか

        /// <summary>held の今フレーム状態を渡し、パルスを発火すべきフレームで true を返す。</summary>
        public bool Update(bool held, float delay, float interval, float dt)
        {
            if (!held)
            {
                m_prevHeld = false;
                m_timer = 0f;
                m_repeating = false;
                return false;
            }

            if (!m_prevHeld)
            {
                // 立ち上がり: 即時 1 発（その後 delay を計測）。
                m_prevHeld = true;
                m_timer = 0f;
                m_repeating = false;
                return true;
            }

            m_timer += dt;
            float threshold = m_repeating ? interval : delay;
            if (m_timer >= threshold)
            {
                m_timer = 0f;
                m_repeating = true;
                return true;
            }
            return false;
        }

        public void Reset()
        {
            m_prevHeld = false;
            m_timer = 0f;
            m_repeating = false;
        }
    }
}
