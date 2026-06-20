namespace BG2VR
{
    /// <summary>
    /// BG2VR が生成する視覚物の専用 layer。ゲームの layer 使用は 0,1,2,4,5,8,9 のみ
    /// （2026-06-06 live 実測）＝10-31 は 30 以外未定義で 29/30 は安全。
    /// UiSceneVoid（UI-only 画面で裏の 3D を非表示）が eye カメラの cullingMask を本 layer 群のみに
    /// 絞っても VR 視覚物が見え続けるための分離。UI カメラの mask は canvas root layer の和集合
    /// （ProjectorRunner.ComputeCullingMask）で本 layer 群を含み得ない＝RT 再帰なし。
    ///
    /// 2 層に分けるのは post-process 反映の選択のため（post 除外は cullingMask の layer 単位でしか効かない）:
    /// - Visuals(30)             : UI パネル / ボタン帯 / レーザー線 / レティクル / Cheki 画面。post から除外し
    ///   くっきり表示（eye main pass から落とし、fork の DrawEyeOverlay が post 後に crisp 重ね描き）。常に最前面。
    /// - VisualsPostProcessed(29): コントローラ・手元モデル・プロップ（遮蔽源）。eye main pass に残し
    ///   ゲームの post（グレーディング+Bloom）を反映（overlayMask=Visuals に含まれない＝重ね描き対象外）。
    ///   選択的深度（VrControllerOccludeUi）では fork が本層だけを遮蔽源にして UI を depth test させる。
    /// </summary>
    public static class VrLayers
    {
        public const int Visuals = 30;
        public const int VisualsMask = 1 << Visuals;

        public const int VisualsPostProcessed = 29;
        public const int VisualsPostProcessedMask = 1 << VisualsPostProcessed;

        /// <summary>手モデル専用 layer。存在意義は fork の SetVrModelOverlay に渡す overlay 描画チャネル選択＝
        /// main pass から除外しつつ overlay pass で最前面描画する（UI=30 と同型）。HandLightingRunner が IsVrActive 時に
        /// この mask を push する。手の照明は同 runner が global uniform（_BG2VR_HandLightDir/Color）で push＝Unity Light
        /// component は spawn せず scene light からも独立。eye の void mask には含めない（overlay channel が描画を担保＝
        /// 含めると main pass で二重描画になる・EyeCullingPolicy 参照）。</summary>
        public const int HandLighting = 28;
        public const int HandLightingMask = 1 << HandLighting;
    }
}
