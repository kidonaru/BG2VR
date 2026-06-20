using BG2VR.PostProcess;
using Xunit;

namespace BG2VR.Tests
{
    public class PostProcessPolicyTests
    {
        // ── ShouldOverride（機能 ON かつ VR 描画中のみ） ───────────────
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void ShouldOverride_は_enabled_かつ_vrActive(bool enabled, bool vrActive, bool expected)
            => Assert.Equal(expected, PostProcessPolicy.ShouldOverride(enabled, vrActive));

        // ── ShouldSuppress（DoF/CA は常に抑制・Vignette は config・他は反映） ───────────────
        [Theory]
        [InlineData("DepthOfField")]
        [InlineData("ChromaticAberration")]
        public void DoF_と_CA_は_keepVignette_に依らず常に抑制(string name)
        {
            Assert.True(PostProcessPolicy.ShouldSuppress(name, keepVignette: true));
            Assert.True(PostProcessPolicy.ShouldSuppress(name, keepVignette: false));
        }

        [Fact]
        public void Vignette_は_keepVignette_で_残す側は抑制しない()
            => Assert.False(PostProcessPolicy.ShouldSuppress("Vignette", keepVignette: true));

        [Fact]
        public void Vignette_は_keepVignette_false_で抑制する()
            => Assert.True(PostProcessPolicy.ShouldSuppress("Vignette", keepVignette: false));

        [Theory]
        [InlineData("Bloom")]
        [InlineData("ColorAdjustments")]
        [InlineData("LiftGammaGain")]
        [InlineData("ShadowsMidtonesHighlights")]
        [InlineData("ColorCurves")]
        [InlineData("Tonemapping")]
        public void グレーディング系と_Bloom_は抑制しない(string name)
        {
            Assert.False(PostProcessPolicy.ShouldSuppress(name, keepVignette: true));
            Assert.False(PostProcessPolicy.ShouldSuppress(name, keepVignette: false));
        }

        // ── AddLayer（global PPE Volume の layer を OR で集約） ───────────────
        [Fact]
        public void AddLayer_は_layer_ビットを_OR_する()
        {
            int mask = 0;
            mask = PostProcessPolicy.AddLayer(mask, 0);   // layer 0 → bit 1
            Assert.Equal(1, mask);
            mask = PostProcessPolicy.AddLayer(mask, 8);   // layer 8 → bit 256 を追加
            Assert.Equal(1 | (1 << 8), mask);
        }

        [Fact]
        public void AddLayer_は_同一_layer_の重複で増えない()
        {
            int mask = PostProcessPolicy.AddLayer(0, 5);
            Assert.Equal(mask, PostProcessPolicy.AddLayer(mask, 5));
        }

        // ── EffectiveSceneMask（本描画から overlay layer を除外） ───────────────
        [Fact]
        public void EffectiveSceneMask_は_overlay_ビットだけ落とす()
        {
            int visuals = 1 << 30;
            Assert.Equal(~visuals, PostProcessPolicy.EffectiveSceneMask(-1, visuals)); // Everything から layer30 を除外
        }

        [Fact]
        public void EffectiveSceneMask_void時_base等しいoverlayで_0()
        {
            int visuals = 1 << 30;
            // void: base=overlay=VisualsMask → 本描画は空（CB が overlay を描く）
            Assert.Equal(0, PostProcessPolicy.EffectiveSceneMask(visuals, visuals));
        }

        [Fact]
        public void EffectiveSceneMask_は_VisualsPostProcessed29を本描画に残しVisuals30だけ落とす()
        {
            // 設計の核: overlay=Visuals(30) のみを落とし、VisualsPostProcessed(29) は本描画に残す
            // ＝レーザー/コントローラ(29)は main pass で post を受け、UI(30)は overlay で crisp 重ね描き。
            // overlayMask に 29 を入れない不変条件（二重描画防止）を純関数層で固定する回帰テスト。
            int baseMask = BG2VR.VrLayers.VisualsMask | BG2VR.VrLayers.VisualsPostProcessedMask;
            Assert.Equal(
                BG2VR.VrLayers.VisualsPostProcessedMask,
                PostProcessPolicy.EffectiveSceneMask(baseMask, BG2VR.VrLayers.VisualsMask));
        }

        // ── IsLayerInMask ───────────────
        [Fact]
        public void IsLayerInMask_は_包含判定する()
        {
            int visuals = 1 << 30;
            Assert.True(PostProcessPolicy.IsLayerInMask(30, visuals));
            Assert.False(PostProcessPolicy.IsLayerInMask(0, visuals));
        }

        // ── ShouldApplyBloomScale（1.0 近傍は no-op＝書き込まない） ───────────────
        [Theory]
        [InlineData(1.0f)]
        [InlineData(0.99995f)]  // 乖離 5e-5 < 1e-4 ＝ no-op
        [InlineData(1.00005f)]
        public void ShouldApplyBloomScale_は_1近傍で書き込まない(float scale)
            => Assert.False(PostProcessPolicy.ShouldApplyBloomScale(scale));

        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.5f)]
        [InlineData(1.5f)]
        [InlineData(2.0f)]
        [InlineData(0.99f)]  // 1e-4 を超える乖離は適用する
        public void ShouldApplyBloomScale_は_1から外れたら適用する(float scale)
            => Assert.True(PostProcessPolicy.ShouldApplyBloomScale(scale));

        // ── ScaledBloomIntensity（ゲーム値 × 倍率） ───────────────
        [Theory]
        [InlineData(6f, 1.0f, 6f)]
        [InlineData(6f, 0.5f, 3f)]
        [InlineData(6f, 1.5f, 9f)]
        [InlineData(6f, 0.0f, 0f)]
        public void ScaledBloomIntensity_は_原値に倍率を掛ける(float original, float scale, float expected)
            => Assert.Equal(expected, PostProcessPolicy.ScaledBloomIntensity(original, scale), 3);
    }
}
