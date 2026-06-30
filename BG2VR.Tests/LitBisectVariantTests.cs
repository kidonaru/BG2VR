using UnityVRMod.Features.VrVisualization.ShaderSimplification;
using Xunit;

namespace BG2VR.Tests
{
    /// <summary>
    /// Round 9I 用 <see cref="LitBisectVariant"/> → HLSL keyword 文字列対応の純関数テスト。
    /// <c>BG2VR/LitBisect</c> shader 側 <c>#pragma multi_compile_local_fragment _ _BG2VR_BISECT_V1 _BG2VR_BISECT_V2 _BG2VR_BISECT_V4</c>
    /// と完全一致していることを固定。
    /// </summary>
    public class LitBisectVariantTests
    {
        [Theory]
        [InlineData(LitBisectVariant.Off, "")]
        [InlineData(LitBisectVariant.V0, "")]
        [InlineData(LitBisectVariant.V1, "_BG2VR_BISECT_V1")]
        [InlineData(LitBisectVariant.V2, "_BG2VR_BISECT_V2")]
        [InlineData(LitBisectVariant.V4, "_BG2VR_BISECT_V4")]
        [InlineData(LitBisectVariant.V0_SameName, "")]
        public void GetKeyword_MapsVariantToHlslKeyword(LitBisectVariant variant, string expected)
        {
            Assert.Equal(expected, LitBisectVariantKeywords.GetKeyword(variant));
        }

        [Theory]
        [InlineData(LitBisectVariant.Off, false)]
        [InlineData(LitBisectVariant.V0, true)]
        [InlineData(LitBisectVariant.V1, true)]
        [InlineData(LitBisectVariant.V2, true)]
        [InlineData(LitBisectVariant.V4, true)]
        [InlineData(LitBisectVariant.V0_SameName, true)]
        public void IsActive_ReportsOffAsInactiveAndAllOthersAsActive(LitBisectVariant variant, bool expected)
        {
            Assert.Equal(expected, LitBisectVariantKeywords.IsActive(variant));
        }

        [Theory]
        [InlineData(LitBisectVariant.Off, false)]
        [InlineData(LitBisectVariant.V0, false)]
        [InlineData(LitBisectVariant.V1, false)]
        [InlineData(LitBisectVariant.V2, false)]
        [InlineData(LitBisectVariant.V4, false)]
        [InlineData(LitBisectVariant.V0_SameName, true)]
        public void IsSameName_TrueOnlyForV0SameName(LitBisectVariant variant, bool expected)
        {
            // bisector が swap 先 shader を BundledShaders.LitBisect / LitBisectSameName から選ぶ key。
            // V0_SameName だけ true・他は false でなければならない。
            Assert.Equal(expected, LitBisectVariantKeywords.IsSameName(variant));
        }

        [Fact]
        public void AllKeywords_ContainsExactlyV1V2V4()
        {
            // 排他 disable で全 variant keyword を OFF にする runtime ループが参照する固定セット。
            // V0_SameName は keyword OFF (= GetKeyword="" / default 経路) のため AllKeywords には含めない。
            Assert.Equal(3, LitBisectVariantKeywords.AllKeywords.Length);
            Assert.Contains("_BG2VR_BISECT_V1", LitBisectVariantKeywords.AllKeywords);
            Assert.Contains("_BG2VR_BISECT_V2", LitBisectVariantKeywords.AllKeywords);
            Assert.Contains("_BG2VR_BISECT_V4", LitBisectVariantKeywords.AllKeywords);
        }

        [Fact]
        public void Constants_MatchHlslKeywordSpelling()
        {
            // HLSL 側 `_BG2VR_BISECT_V*` と一致しないと per-material EnableKeyword が無効になる。typo 検出用。
            Assert.Equal("_BG2VR_BISECT_V1", LitBisectVariantKeywords.V1Keyword);
            Assert.Equal("_BG2VR_BISECT_V2", LitBisectVariantKeywords.V2Keyword);
            Assert.Equal("_BG2VR_BISECT_V4", LitBisectVariantKeywords.V4Keyword);
        }

        [Fact]
        public void EnumOrder_PreservesUnderlyingIndexForVrModEnumAccessor()
        {
            // F10 dropdown は Enum.GetValues(typeof(LitBisectVariant)) の index 経由で VrModEnumAccessor が参照する。
            // 末尾追加 (V0_SameName) で既存 Off=0/V0=1/V1=2/V2=3/V4=4 を保持していることを固定する。
            Assert.Equal(0, (int)LitBisectVariant.Off);
            Assert.Equal(1, (int)LitBisectVariant.V0);
            Assert.Equal(2, (int)LitBisectVariant.V1);
            Assert.Equal(3, (int)LitBisectVariant.V2);
            Assert.Equal(4, (int)LitBisectVariant.V4);
            Assert.Equal(5, (int)LitBisectVariant.V0_SameName);
        }
    }
}
