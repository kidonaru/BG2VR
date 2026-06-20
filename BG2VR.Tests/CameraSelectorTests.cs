using System.Collections.Generic;
using BG2VR.CameraBridge;
using Xunit;

namespace BG2VR.Tests
{
    public class CameraSelectorTests
    {
        // 3D を描く既定 cullingMask（UI 以外のビットを含む）。
        private const int ThreeDMask = ~0; // Everything

        private static CameraCandidate Cam(int index, float depth,
            bool activeAndEnabled = true, bool hasRT = false, int cullingMask = ThreeDMask, bool nameExcluded = false)
            => new CameraCandidate(index, activeAndEnabled, hasRT, cullingMask, depth, nameExcluded);

        [Fact]
        public void 空なら該当なし()
        {
            Assert.Equal(-1, CameraSelector.SelectBestIndex(new List<CameraCandidate>()));
        }

        [Fact]
        public void 有効な3Dカメラのうちdepth最大を選ぶ()
        {
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: -1f),
                Cam(1, depth: 5f),
                Cam(2, depth: 2f),
            };
            Assert.Equal(1, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void RT描画カメラは除外_VR_eyeやpostFX対策()
        {
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: 10f, hasRT: true), // 高 depth だが RT → 除外
                Cam(1, depth: 1f),
            };
            Assert.Equal(1, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void 無効カメラは除外()
        {
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: 10f, activeAndEnabled: false),
                Cam(1, depth: 1f),
            };
            Assert.Equal(1, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void 名前除外カメラは選ばない_VR_rig対策()
        {
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: 10f, nameExcluded: true),
                Cam(1, depth: 1f),
            };
            Assert.Equal(1, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void UI専用カメラは除外()
        {
            int uiOnly = CameraSelector.UILayerMask; // UI ビットのみ
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: 10f, cullingMask: uiOnly),
                Cam(1, depth: 1f, cullingMask: ThreeDMask),
            };
            Assert.Equal(1, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void UIを含むが3Dも描くカメラは有効()
        {
            int uiPlus3D = CameraSelector.UILayerMask | (1 << 0); // UI + Default
            var list = new List<CameraCandidate> { Cam(0, depth: 3f, cullingMask: uiPlus3D) };
            Assert.Equal(0, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void depth同値は添字が小さい方()
        {
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: 2f),
                Cam(1, depth: 2f),
            };
            Assert.Equal(0, CameraSelector.SelectBestIndex(list));
        }

        [Fact]
        public void 有効候補が無ければ該当なし()
        {
            var list = new List<CameraCandidate>
            {
                Cam(0, depth: 5f, hasRT: true),
                Cam(1, depth: 3f, activeAndEnabled: false),
            };
            Assert.Equal(-1, CameraSelector.SelectBestIndex(list));
        }
    }
}
