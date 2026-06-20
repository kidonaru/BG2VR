using BG2VR;
using BG2VR.EyeCulling;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests
{
    public class EyeCullingPolicyTests
    {
        [Fact]
        public void Normal_RendersEverythingWithSkybox()
        {
            var s = EyeCullingPolicy.Resolve(voidActive: false, dimActive: false, voidBrightness: 0.05f, dimBrightness: 0.1f);
            Assert.Equal(-1, s.CullingMask);
            Assert.Equal(CameraClearFlags.Skybox, s.ClearFlags);
        }

        [Fact]
        public void Void_RendersBothVisualLayersWithSolidColorDark()
        {
            var s = EyeCullingPolicy.Resolve(voidActive: true, dimActive: false, voidBrightness: 0.05f, dimBrightness: 0.1f);
            // UI(30) + レーザー/コントローラ(29) の両層（片方欠けると UI-only 画面で消える）。
            Assert.Equal(VrLayers.VisualsMask | VrLayers.VisualsPostProcessedMask, s.CullingMask);
            Assert.Equal(CameraClearFlags.SolidColor, s.ClearFlags);
            Assert.Equal(new Color(0.05f, 0.05f, 0.05f, 1f), s.BackgroundColor);
        }

        [Fact]
        public void Dim_KeepsEverythingButSolidColorDark()
        {
            var s = EyeCullingPolicy.Resolve(voidActive: false, dimActive: true, voidBrightness: 0.05f, dimBrightness: 0.1f);
            Assert.Equal(-1, s.CullingMask);
            Assert.Equal(CameraClearFlags.SolidColor, s.ClearFlags);
            Assert.Equal(new Color(0.1f, 0.1f, 0.1f, 1f), s.BackgroundColor);
        }

        [Fact]
        public void VoidWinsOverDim()
        {
            var s = EyeCullingPolicy.Resolve(voidActive: true, dimActive: true, voidBrightness: 0.05f, dimBrightness: 0.1f);
            Assert.Equal(VrLayers.VisualsMask | VrLayers.VisualsPostProcessedMask, s.CullingMask);
            Assert.Equal(new Color(0.05f, 0.05f, 0.05f, 1f), s.BackgroundColor); // void 明度（dim ではない）
        }
    }
}
