using BG2VR.VrFade;
using Xunit;

namespace BG2VR.Tests;

public class TransitionOverlayPolicyTests
{
    private const float W = 2.5f;
    private const float D = 1.2f;

    // 非表示 → 表示エッジは alpha が微小でも必ず push。
    [Fact]
    public void Show_edge_pushes()
    {
        var d = TransitionOverlayPolicy.Decide(true, true, 0.001f, W, D, false, 0f, W, D);
        Assert.True(d.ShouldPush);
        Assert.True(d.Visible);
        Assert.Equal(0.001f, d.Alpha);
    }

    // 表示 → 非表示エッジ（fade GO 非 active 化）は必ず push。
    [Fact]
    public void Hide_edge_pushes()
    {
        var d = TransitionOverlayPolicy.Decide(true, false, 0f, W, D, true, 1f, W, D);
        Assert.True(d.ShouldPush);
        Assert.False(d.Visible);
    }

    // config OFF はミラー中でも hide エッジになる（live トグル）。
    [Fact]
    public void Disabled_hides_even_if_active()
    {
        var d = TransitionOverlayPolicy.Decide(false, true, 0.5f, W, D, true, 0.5f, W, D);
        Assert.True(d.ShouldPush);
        Assert.False(d.Visible);
    }

    // 可視継続中の微小 alpha 変化（ε 以下）は間引く（DOTween 毎フレ対策）。
    [Fact]
    public void Tiny_alpha_delta_skipped()
    {
        var d = TransitionOverlayPolicy.Decide(true, true, 0.5f + TransitionOverlayPolicy.Epsilon * 0.5f, W, D, true, 0.5f, W, D);
        Assert.False(d.ShouldPush);
    }

    // 可視継続中の有意な alpha 変化は push。
    [Fact]
    public void Visible_alpha_delta_pushes()
    {
        var d = TransitionOverlayPolicy.Decide(true, true, 0.6f, W, D, true, 0.5f, W, D);
        Assert.True(d.ShouldPush);
        Assert.True(d.Visible);
        Assert.Equal(0.6f, d.Alpha);
    }

    // width/distance の live 変更（F10 スライダー実機チューニング）も push 対象。
    [Fact]
    public void Size_change_pushes_while_visible()
    {
        var d = TransitionOverlayPolicy.Decide(true, true, 0.5f, W + 1f, D, true, 0.5f, W, D);
        Assert.True(d.ShouldPush);
    }

    // 非表示が継続している間（通常プレイ中）は push しない。
    [Fact]
    public void Idle_invisible_no_push()
    {
        var d = TransitionOverlayPolicy.Decide(true, false, 0f, W, D, false, 0f, W, D);
        Assert.False(d.ShouldPush);
    }

    // active でも alpha 0（fade 開始フレーム）はまだ非表示扱い＝0 alpha overlay の 1 フレ表示を防ぐ。
    [Fact]
    public void Active_zero_alpha_stays_hidden()
    {
        var d = TransitionOverlayPolicy.Decide(true, true, 0f, W, D, false, 0f, W, D);
        Assert.False(d.ShouldPush);
        Assert.False(d.Visible);
    }
}
