using BG2VR.FramePacing;
using Xunit;

namespace BG2VR.Tests;

public class FramePacingPolicyTests
{
    // 全 16 入力の truth table。優先順位: rising > falling > reassert。
    // - rising edge（prev=F, eff=T）は reassert/diverged に関わらず CaptureAndApply（capture が apply より先）
    // - falling edge（prev=T, eff=F）は無条件 Restore
    // - steady ON（prev=T, eff=T）は reassert ON かつ乖離検出時のみ Reassert
    // - steady OFF（prev=F, eff=F）は常に None（非 VR / config OFF 時に何も書かない保証）
    [Theory]
    // prevEff, eff,   reassert, diverged, expected
    [InlineData(false, false, false, false, FramePacingAction.None)]
    [InlineData(false, false, false, true,  FramePacingAction.None)]
    [InlineData(false, false, true,  false, FramePacingAction.None)]
    [InlineData(false, false, true,  true,  FramePacingAction.None)]
    [InlineData(false, true,  false, false, FramePacingAction.CaptureAndApply)]
    [InlineData(false, true,  false, true,  FramePacingAction.CaptureAndApply)]
    [InlineData(false, true,  true,  false, FramePacingAction.CaptureAndApply)]
    [InlineData(false, true,  true,  true,  FramePacingAction.CaptureAndApply)]
    [InlineData(true,  false, false, false, FramePacingAction.Restore)]
    [InlineData(true,  false, false, true,  FramePacingAction.Restore)]
    [InlineData(true,  false, true,  false, FramePacingAction.Restore)]
    [InlineData(true,  false, true,  true,  FramePacingAction.Restore)]
    [InlineData(true,  true,  false, false, FramePacingAction.None)]
    [InlineData(true,  true,  false, true,  FramePacingAction.None)] // re-assert OFF: 乖離しても放置（config の意図）
    [InlineData(true,  true,  true,  false, FramePacingAction.None)] // re-assert ON: 乖離なし → 書き込みゼロ
    [InlineData(true,  true,  true,  true,  FramePacingAction.Reassert)]
    public void Evaluate_truth_table(
        bool prevEffective, bool effective, bool reassert, bool diverged, FramePacingAction expected)
    {
        Assert.Equal(expected, FramePacingPolicy.Evaluate(prevEffective, effective, reassert, diverged));
    }

    [Fact]
    public void Transition_teardown_round_trip_restores_then_recaptures()
    {
        // 遷移 rig-teardown の往復: VR 中 → falling(Restore) → rising(CaptureAndApply)。
        // per-edge capture の前提（rising のたびに取り直す）が成立する遷移列であることを確認。
        Assert.Equal(FramePacingAction.Restore, FramePacingPolicy.Evaluate(true, false, false, false));
        Assert.Equal(FramePacingAction.CaptureAndApply, FramePacingPolicy.Evaluate(false, true, false, false));
    }

    [Fact]
    public void Config_toggle_mid_vr_behaves_as_effective_edge()
    {
        // VR 中に UncapFrameRate を OFF→ON / ON→OFF した場合も effective の edge として扱われる
        // （呼び出し側で effective = IsVrActive && UncapFrameRate に畳んで渡す契約）。
        Assert.Equal(FramePacingAction.Restore, FramePacingPolicy.Evaluate(true, false, true, false));   // ON→OFF
        Assert.Equal(FramePacingAction.CaptureAndApply, FramePacingPolicy.Evaluate(false, true, true, false)); // OFF→ON
    }
}
