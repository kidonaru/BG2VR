using BG2VR.VrInput;
using Xunit;

namespace BG2VR.Tests;

public class PointerButtonStateTests
{
    [Fact]
    public void Detects_press_hold_release_edges()
    {
        var s = new PointerButtonState();

        var e0 = s.Update(false);
        Assert.False(e0.Pressed); Assert.False(e0.JustPressed); Assert.False(e0.JustReleased);

        var e1 = s.Update(true);  // 立ち上がり
        Assert.True(e1.Pressed); Assert.True(e1.JustPressed); Assert.False(e1.JustReleased);

        var e2 = s.Update(true);  // 保持
        Assert.True(e2.Pressed); Assert.False(e2.JustPressed); Assert.False(e2.JustReleased);

        var e3 = s.Update(false); // 立ち下がり
        Assert.False(e3.Pressed); Assert.False(e3.JustPressed); Assert.True(e3.JustReleased);

        var e4 = s.Update(false); // 離し継続
        Assert.False(e4.Pressed); Assert.False(e4.JustPressed); Assert.False(e4.JustReleased);
    }

    [Fact]
    public void Reset_clears_previous_state()
    {
        var s = new PointerButtonState();
        s.Update(true);
        s.Reset();
        var e = s.Update(true); // Reset 後の true は再び立ち上がり扱い
        Assert.True(e.JustPressed);
    }
}
