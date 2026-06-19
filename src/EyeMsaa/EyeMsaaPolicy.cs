namespace BG2VR.EyeMsaa
{
    /// <summary>EyeMsaaRunner が 1 フレームに実行すべき操作。</summary>
    public enum EyeMsaaAction { None, CaptureAndApply, Apply, Restore }

    /// <summary>
    /// VR 中に URP msaaSampleCount を config 値へ駆動する判断ロジック（純関数）。
    /// effective = 「VR rig 描画可能（IsVrActive）かつ XR セッション running」を呼び出し側で解決して渡す。
    /// rising edge で現 URP 値を capture して desired を適用、steady 中に現値≠desired なら Apply（live 反映）、
    /// falling edge で capture 値へ復元（URP msaa はパイプライン全体＝desktop にも効くグローバル設定のため）。
    /// steady の Apply は「VR 中 MSAA は BG2VR 単独所有」の常時収束＝dropdown live 変更も外部書き換えも
    /// config 値へ戻す（FramePacing の reassert を常時 ON にした相当。通常は rising の 1 回適用で足りる）。
    /// </summary>
    public static class EyeMsaaPolicy
    {
        public static EyeMsaaAction Evaluate(bool prevEffective, bool effective, int currentMsaa, int desiredMsaa)
        {
            if (effective && !prevEffective) return EyeMsaaAction.CaptureAndApply; // capture 優先
            if (!effective && prevEffective) return EyeMsaaAction.Restore;
            if (effective && currentMsaa != desiredMsaa) return EyeMsaaAction.Apply; // dropdown live 変更 / 外部書き換えの打ち消し
            return EyeMsaaAction.None;
        }
    }
}
