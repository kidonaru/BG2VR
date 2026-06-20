using BG2VR;
using BG2VR.VrInput;
using Xunit;

namespace BG2VR.Tests
{
    /// <summary>
    /// HandLayerResolver の純関数テスト（UnityEngine 非依存）。
    /// Hand=HandLighting(28) 固定 / 他種別=VisualsPostProcessed(29) 固定の契約。
    /// scene 全 light は cullingMask に 28 bit 立てておらず、手は BG2VR 自前 directional light のみで照らされる。
    /// </summary>
    public class HandLayerResolverTests
    {
        [Fact]
        public void Hand_AlwaysHandLighting()
        {
            Assert.Equal(VrLayers.HandLighting, HandLayerResolver.Resolve(HandModelKind.Hand));
        }

        [Theory]
        [InlineData(HandModelKind.Controller)]
        [InlineData(HandModelKind.Camera)]
        [InlineData(HandModelKind.Tambourine)]
        [InlineData(HandModelKind.GlowStick)]
        public void NonHand_AlwaysVisualsPostProcessed(HandModelKind kind)
        {
            Assert.Equal(VrLayers.VisualsPostProcessed, HandLayerResolver.Resolve(kind));
        }
    }
}
