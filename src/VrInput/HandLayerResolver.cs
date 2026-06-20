namespace BG2VR.VrInput
{
    /// <summary>
    /// 手モデル GO のレイヤーを決める純関数（UnityEngine 非依存）。
    /// Hand 種別は HandLighting(28) 固定＝fork SetVrModelOverlay の overlay 描画チャネルに乗せ、main pass 除外+
    /// overlay pass で最前面描画する。手の照明は BG2VR.HandLighting.HandLightingRunner が global uniform で push＝
    /// Unity Light component は使わず scene light からも独立（VIP/Title/Bar 等すべて同じ温かみで安定描画）。
    /// 他種別（コントローラ/カメラ/タンバリン/サイリウム）は従来通り VisualsPostProcessed(29) 固定。
    /// </summary>
    internal static class HandLayerResolver
    {
        /// <summary>種別ごとの GO レイヤーを返す。Hand=HandLighting(28) / 他=VisualsPostProcessed(29)。</summary>
        public static int Resolve(HandModelKind kind)
        {
            if (kind != HandModelKind.Hand) return VrLayers.VisualsPostProcessed;
            return VrLayers.HandLighting;
        }
    }
}
