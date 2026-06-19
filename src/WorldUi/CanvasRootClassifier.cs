namespace BG2VR.WorldUi
{
    /// <summary>
    /// 1 つの Canvas が world 投影対象の root か判定する純関数（bool ロジックのみ・UnityEngine 非依存）。
    /// 対象 = live（prefab でない）かつ active かつ Overlay かつ GraphicRaycaster を持つ。
    /// 除外は自然に成立: WorldSpace(=既に3D, isOverlay=false) / フェード(raycaster無) / Debug(非active) / prefab(非live)。
    /// RenderMode→isOverlay 変換・nested 判定・ツール除外・探索は CanvasRootResolver（ランタイム）側。
    /// </summary>
    public static class CanvasRootClassifier
    {
        public static bool IsProjectableRoot(bool isLive, bool isActive, bool isOverlay, bool hasRaycaster)
            => isLive && isActive && isOverlay && hasRaycaster;
    }
}
