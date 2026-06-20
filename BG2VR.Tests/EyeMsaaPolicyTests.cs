using BG2VR.EyeMsaa;
using Xunit;

namespace BG2VR.Tests
{
    public class EyeMsaaPolicyTests
    {
        [Fact]
        public void Rising_edge_は_CaptureAndApply()
            => Assert.Equal(EyeMsaaAction.CaptureAndApply, EyeMsaaPolicy.Evaluate(false, true, 1, 4));

        [Fact]
        public void Falling_edge_は_Restore()
            => Assert.Equal(EyeMsaaAction.Restore, EyeMsaaPolicy.Evaluate(true, false, 4, 4));

        [Fact]
        public void Steady_で_config_変更_は_Apply()
            => Assert.Equal(EyeMsaaAction.Apply, EyeMsaaPolicy.Evaluate(true, true, 4, 8));

        [Fact]
        public void Steady_で_一致_は_None()
            => Assert.Equal(EyeMsaaAction.None, EyeMsaaPolicy.Evaluate(true, true, 4, 4));

        [Fact]
        public void VR非アクティブ継続_は_None()
            => Assert.Equal(EyeMsaaAction.None, EyeMsaaPolicy.Evaluate(false, false, 1, 4));
    }
}
