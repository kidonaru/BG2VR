namespace BG2VR.VrInput
{
    /// <summary>
    /// 「grip 保持中のトリガー rising edge」でモデル切替を 1 回発火する per-hand 検出（純ロジック）。
    /// grip 中はトリガーが arbiter/UI へ渡らない（ProjectorRunner でゼロ化・suppress）ため
    /// UI クリック/ポインタ切替と競合しない。押しっぱなしでは多重発火しない（edge）。
    /// 入力は呼び出し側で valid ゲート済み（invalid 手は grip/trig=false で渡す＝PointerHandArbiter と同方針・
    /// invalid 中は偽 rising を出さない）。grip より先にトリガーが押されていた場合は prevTrig=true ＝発火しない
    /// （combo は「grip 保持 → トリガーを引く」の順のみ）。
    /// </summary>
    public sealed class ModelSwitchInput
    {
        private bool m_prevTrigLeft;
        private bool m_prevTrigRight;

        /// <summary>各手の grip(保持) と trigger(押下) から、このフレームで切替を発火すべきかを返す。</summary>
        public void Update(bool gripLeft, bool trigLeft, bool gripRight, bool trigRight,
            out bool cycleLeft, out bool cycleRight)
        {
            cycleLeft = gripLeft && trigLeft && !m_prevTrigLeft;
            cycleRight = gripRight && trigRight && !m_prevTrigRight;
            m_prevTrigLeft = trigLeft;
            m_prevTrigRight = trigRight;
        }

        public void Reset()
        {
            m_prevTrigLeft = false;
            m_prevTrigRight = false;
        }
    }
}
