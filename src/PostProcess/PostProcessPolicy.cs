namespace BG2VR.PostProcess
{
    /// <summary>
    /// ゲームの URP post-process を VR 両眼へ反映する際の判断を担う純関数群
    /// （UnityEngine/BepInEx/ゲーム型 非依存＝xUnit テスト可能）。
    ///
    /// スコープ: カラーグレーディング + Bloom は反映する。DepthOfField / ChromaticAberration は VR 快適性
    /// （酔い・レンズ補正二重がけ）のため常に抑制する。Vignette は config（keepVignette）で残す/外すを選ぶ。
    /// 実際の Volume 列挙・抑制・eye への push は PostProcessCoordinator（reflection・非純粋）が行う。
    /// </summary>
    public static class PostProcessPolicy
    {
        /// <summary>
        /// VR eye の post-process override を出すべきか（= 機能 ON かつ VR 描画中）。
        /// </summary>
        public static bool ShouldOverride(bool enabled, bool vrActive) => enabled && vrActive;

        /// <summary>
        /// 指定 VolumeComponent 型名を eye 描画で抑制すべきか。
        /// DepthOfField / ChromaticAberration は常に抑制。Vignette は keepVignette が false のとき抑制。
        /// それ以外（Bloom / ColorAdjustments / LiftGammaGain / ShadowsMidtonesHighlights / ColorCurves 等）は反映する。
        /// </summary>
        /// <param name="componentTypeName">VolumeComponent の <c>GetType().Name</c>（例 "DepthOfField"）。</param>
        public static bool ShouldSuppress(string componentTypeName, bool keepVignette)
        {
            switch (componentTypeName)
            {
                case "DepthOfField":
                case "ChromaticAberration":
                    return true;
                case "Vignette":
                    return !keepVignette;
                default:
                    return false;
            }
        }

        /// <summary>
        /// ブルーム倍率を実際に適用すべきか（1.0 近傍は no-op＝ゲーム native のまま書き込まない）。
        /// 既定 1.0 で完全 no-op にすることでゲーム側 bloom への pin と回帰を避ける（Coordinator の dirty 遷移と対）。
        /// </summary>
        public static bool ShouldApplyBloomScale(float scale) => System.Math.Abs(scale - 1f) > 1e-4f;

        /// <summary>
        /// ゲームの bloom intensity × 倍率。負にはならない（slider 下限 0・MinFloatParameter 下限 0 のため追加 clamp 不要）。
        /// </summary>
        public static float ScaledBloomIntensity(float original, float scale) => original * scale;

        /// <summary>
        /// eye の volumeLayerMask に layer を 1 つ加える（global PPE Volume の存在 layer を OR で集約する用）。
        /// </summary>
        public static int AddLayer(int mask, int layer) => mask | (1 << layer);

        /// <summary>
        /// 本描画（post 付き）の cullingMask。overlay layer（VR ビジュアル）を除外する。
        /// fork はこの mask でシーンのみ描き、overlay は post 後に CommandBuffer で重ねる。
        /// 不変条件: void 時 base=overlay=VisualsMask なら結果 0（本描画は空＝clear のみ）。
        /// </summary>
        public static int EffectiveSceneMask(int baseMask, int overlayMask) => baseMask & ~overlayMask;

        /// <summary>layer が mask に含まれるか（CB に積む overlay renderer の判定用）。</summary>
        public static bool IsLayerInMask(int layer, int mask) => (mask & (1 << layer)) != 0;
    }
}
