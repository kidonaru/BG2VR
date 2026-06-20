namespace BG2VR.FramePacing
{
    /// <summary>FramePacingRunner が 1 フレームに実行すべき操作。</summary>
    public enum FramePacingAction
    {
        None,
        CaptureAndApply,
        Reassert,
        Restore,
    }

    /// <summary>
    /// VR 中フレームレートキャップ解除の判断ロジック（純関数）。
    /// effective = 「VR rig が描画可能（IsVrActive）かつ XR セッション running（runtime スロットル有効）
    /// かつ UncapFrameRate config ON」を呼び出し側で解決して渡す。
    /// Why: ゲーム本体（GBSystem.Setup）と FixMod（SetRefreshRatePatch）が targetFrameRate=60 を設定したまま
    /// VR 出力されると、72Hz HMD でコンポジタが常時リプロジェクション補間を挟み周期ちらつきになる
    /// （2026-06-06 実機確定。-1 適用で WaitGetPoses が HMD リフレッシュに同期し解消）。
    /// </summary>
    public static class FramePacingPolicy
    {
        public static FramePacingAction Evaluate(
            bool prevEffective, bool effective, bool reassertEveryFrame, bool valuesDiverged)
        {
            // rising edge: capture が apply より先（優先度最上位。diverged は steady 時のみ意味を持つ）
            if (effective && !prevEffective) return FramePacingAction.CaptureAndApply;
            // falling edge: capture 値へ復元
            if (!effective && prevEffective) return FramePacingAction.Restore;
            // steady + re-assert ON + 外部が再設定 → 打ち消し
            if (effective && reassertEveryFrame && valuesDiverged) return FramePacingAction.Reassert;
            return FramePacingAction.None;
        }
    }
}
