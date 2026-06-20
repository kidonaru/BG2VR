using BG2VR.VrFade;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests;

public class FadeMirrorPolicyTests
{
    // image active → image の色（黒/白フェード本体・alpha 含む）。
    [Fact]
    public void Image_active_uses_image_color()
    {
        var c = FadeMirrorPolicy.EvaluateTarget(true, new Color(0, 0, 0, 0.5f), false, 0f);
        Assert.Equal(new Color(0, 0, 0, 0.5f), c);
    }

    // image は transition より優先（ゲーム側は相互排他だが万一の同時 active でも決定的）。
    [Fact]
    public void Image_wins_over_transition()
    {
        var c = FadeMirrorPolicy.EvaluateTarget(true, new Color(1, 1, 1, 0.3f), true, 0.9f);
        Assert.Equal(new Color(1, 1, 1, 0.3f), c);
    }

    // transition のみ → 白 + その alpha（柄 wipe は単色近似）。
    [Fact]
    public void Transition_maps_to_white()
    {
        var c = FadeMirrorPolicy.EvaluateTarget(false, default, true, 0.7f);
        Assert.Equal(new Color(1, 1, 1, 0.7f), c);
    }

    // 両方非 active → クリア(alpha 0)。
    [Fact]
    public void Inactive_clears()
    {
        var c = FadeMirrorPolicy.EvaluateTarget(false, default, false, 0f);
        Assert.Equal(0f, c.a);
    }

    // ε 未満の変化は push しない（DOTween 毎フレ微小変化の間引き）。
    [Fact]
    public void Tiny_delta_skips_push()
    {
        var d = FadeMirrorPolicy.Decide(new Color(0, 0, 0, 0.5005f), new Color(0, 0, 0, 0.5f));
        Assert.False(d.ShouldPush);
    }

    // ε 超は push する。
    [Fact]
    public void Visible_delta_pushes()
    {
        var d = FadeMirrorPolicy.Decide(new Color(0, 0, 0, 0.6f), new Color(0, 0, 0, 0.5f));
        Assert.True(d.ShouldPush);
    }

    // alpha 0 終端は差が ε 以下でも必ず push（compositor への黒残留＝視界喪失の防止）。
    [Fact]
    public void Clear_edge_always_pushes()
    {
        var d = FadeMirrorPolicy.Decide(new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.002f));
        Assert.True(d.ShouldPush);
    }

    // クリア済み同士は push しない（アイドル時の毎フレ native 呼び出し回避）。
    [Fact]
    public void Stable_clear_skips_push()
    {
        var d = FadeMirrorPolicy.Decide(new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0f));
        Assert.False(d.ShouldPush);
    }
}
