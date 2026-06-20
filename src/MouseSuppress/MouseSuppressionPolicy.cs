namespace BG2VR.MouseSuppress
{
    /// <summary>MouseSuppressionRunner が 1 フレームに実行すべき操作。</summary>
    public enum MouseSuppressionAction
    {
        None,
        Disable,
        Enable,
        Reassert,
    }

    /// <summary>
    /// VR 中マウス無効化の判断ロジック（純関数）。
    /// effective =「VR rig 描画可能（IsVrActive）かつ SuppressMouseInVr config ON」を呼び出し側で解決して渡す。
    /// needsReassert =「effective 定常中に、まだ無効化していない有効な Mouse.current が存在する」を同じく解決して渡す
    ///（VR 中にマウスが接続/再接続/差し替わった場合の取りこぼしを塞ぐ＝要件「全面無効」。FramePacingPolicy の
    /// diverged 自己修復と同型）。
    /// FramePacingPolicy と同型の edge + 定常再適用判定（rising=Disable / falling=Enable / steady+要再適用=Reassert）。
    /// </summary>
    public static class MouseSuppressionPolicy
    {
        public static MouseSuppressionAction Decide(bool prevEffective, bool effective, bool needsReassert)
        {
            // rising edge: VR 描画開始 → マウス無効化（最優先）
            if (effective && !prevEffective) return MouseSuppressionAction.Disable;
            // falling edge: VR 終了 / HMD doff → マウス復元
            if (!effective && prevEffective) return MouseSuppressionAction.Enable;
            // steady-on: VR 中に現れた/差し替わった有効マウスを再無効化
            if (effective && needsReassert) return MouseSuppressionAction.Reassert;
            return MouseSuppressionAction.None;
        }
    }
}
