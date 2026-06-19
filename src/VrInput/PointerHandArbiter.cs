namespace BG2VR.VrInput
{
    /// <summary>
    /// 「最後にトリガー onset に触れた手」をポインタ手とする調停（System のみ依存・純ロジック）。
    /// 切替は非ポインタ手の onset rising edge のみ（継続押し・両手同時 rising では現状維持＝揺れ防止）。
    /// invalid 手の入力は無視（invalid 中の押下は valid 復帰フレームで rising になる＝意図動作）。初期=右。
    /// </summary>
    public sealed class PointerHandArbiter
    {
        private bool m_pointerIsLeft; // 初期 false=右
        private bool m_prevLeftOnset;
        private bool m_prevRightOnset;

        public bool PointerIsLeft => m_pointerIsLeft;

        /// <summary>戻り値: このフレームでポインタ手が切り替わったか（呼び出し側が pointer 状態をリセットする）。</summary>
        public bool Update(bool leftValid, float leftTrigger, bool rightValid, float rightTrigger, float onsetThreshold)
        {
            bool leftOnset = leftValid && leftTrigger >= onsetThreshold;
            bool rightOnset = rightValid && rightTrigger >= onsetThreshold;
            bool leftRising = leftOnset && !m_prevLeftOnset;
            bool rightRising = rightOnset && !m_prevRightOnset;
            m_prevLeftOnset = leftOnset;
            m_prevRightOnset = rightOnset;

            if (leftRising && rightRising) return false; // 同時 rising は現状維持
            if (leftRising && !m_pointerIsLeft) { m_pointerIsLeft = true; return true; }
            if (rightRising && m_pointerIsLeft) { m_pointerIsLeft = false; return true; }
            return false;
        }

        public void Reset()
        {
            m_pointerIsLeft = false;
            m_prevLeftOnset = false;
            m_prevRightOnset = false;
        }
    }
}
