using Xunit;
using BG2VR.MouseSuppress;

namespace BG2VR.Tests
{
    public class MouseSuppressionPolicyTests
    {
        // rising edge は needsReassert に関わらず Disable が最優先。
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RisingEdge_Disables(bool needsReassert)
            => Assert.Equal(MouseSuppressionAction.Disable,
                MouseSuppressionPolicy.Decide(prevEffective: false, effective: true, needsReassert: needsReassert));

        // falling edge は needsReassert に関わらず Enable。
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FallingEdge_Enables(bool needsReassert)
            => Assert.Equal(MouseSuppressionAction.Enable,
                MouseSuppressionPolicy.Decide(prevEffective: true, effective: false, needsReassert: needsReassert));

        [Fact]
        public void SteadyOff_None()
            => Assert.Equal(MouseSuppressionAction.None,
                MouseSuppressionPolicy.Decide(prevEffective: false, effective: false, needsReassert: false));

        [Fact]
        public void SteadyOn_NoReassert_None()
            => Assert.Equal(MouseSuppressionAction.None,
                MouseSuppressionPolicy.Decide(prevEffective: true, effective: true, needsReassert: false));

        [Fact]
        public void SteadyOn_NeedsReassert_Reasserts()
            => Assert.Equal(MouseSuppressionAction.Reassert,
                MouseSuppressionPolicy.Decide(prevEffective: true, effective: true, needsReassert: true));

        // 非 effective 中の needsReassert は無視（マウス接続検知だけで無効化しない）。
        [Fact]
        public void SteadyOff_NeedsReassert_Ignored_None()
            => Assert.Equal(MouseSuppressionAction.None,
                MouseSuppressionPolicy.Decide(prevEffective: false, effective: false, needsReassert: true));
    }
}
