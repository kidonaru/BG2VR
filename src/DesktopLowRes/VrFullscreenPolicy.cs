namespace BG2VR.DesktopLowRes
{
    /// <summary>VR デスクトップフルスクリーン化 runner が取るアクション。</summary>
    /// <remarks>public は xUnit の [InlineData] 引数に渡すため（public テストメソッドの引数型は public 必須・CS0051 回避）。</remarks>
    public enum VrFullscreenAction
    {
        None,            // 何もしない
        ForceFullscreen, // windowed → FULL_SCREEN に切替（元の DisplaySize を退避）
        Restore,         // 退避した DisplaySize へ復元
    }

    /// <summary>
    /// VR-active と機能 ON / 既に強制済みか / 現在 windowed かの 3 値から、フルスクリーン化 runner の
    /// 次アクションを決める純状態機械。ゲーム型（DisplaySize）に依存せず bool で表現するため xUnit 対象。
    /// 「既にフルスクリーンなら何もしない（forced を立てない）」ので、その場合 VR off でも余計な復元をしない。
    /// </summary>
    internal static class VrFullscreenPolicy
    {
        /// <param name="want">VR-active かつ機能 ON。</param>
        /// <param name="forced">当 runner が既にフルスクリーン化済み（要復元）か。</param>
        /// <param name="currentWindowed">現在の DisplaySize が windowed（≠FULL_SCREEN）か。</param>
        public static VrFullscreenAction Decide(bool want, bool forced, bool currentWindowed)
        {
            if (want && !forced && currentWindowed) return VrFullscreenAction.ForceFullscreen;
            if (!want && forced) return VrFullscreenAction.Restore;
            return VrFullscreenAction.None;
        }
    }
}
