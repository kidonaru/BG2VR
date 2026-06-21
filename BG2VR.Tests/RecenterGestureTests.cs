using Xunit;
using BG2VR.Locomotion;

public class RecenterGestureTests
{
    // 入口フレームは timer を加算しない（Idle→Holding で t=0）。以降のフレームで dt を積む。
    [Fact]
    public void HoldSecs未満では発火しない()
    {
        var g = new RecenterGesture();
        Assert.False(g.Update(true, true, true, true, 0.5f)); // Idle→Holding, t=0
        Assert.False(g.Update(true, true, true, true, 0.4f)); // t=0.4 < 1.0
    }

    [Fact]
    public void 両手Grip継続でちょうど1回発火する()
    {
        var g = new RecenterGesture();
        Assert.False(g.Update(true, true, true, true, 1.0f)); // Idle→Holding, t=0
        Assert.True(g.Update(true, true, true, true, 1.0f));  // t=1.0 ≥ 1.0 → fire
        Assert.False(g.Update(true, true, true, true, 1.0f)); // Fired → 再発火しない
    }

    [Fact]
    public void fire後に保持を続けても再発火しない()
    {
        var g = new RecenterGesture();
        g.Update(true, true, true, true, 1.0f);
        Assert.True(g.Update(true, true, true, true, 1.0f));  // fire
        Assert.False(g.Update(true, true, true, true, 5.0f)); // 保持継続でも false
    }

    [Fact]
    public void 片手だけ離すと発火しない()
    {
        var g = new RecenterGesture();
        Assert.False(g.Update(true, true, true, false, 2.0f)); // 右 grip false
        Assert.False(g.Update(true, false, true, true, 2.0f)); // 左 grip false
        Assert.False(g.Update(true, true, false, true, 2.0f)); // 右 invalid
    }

    [Fact]
    public void fire前に片手を離すとタイマーがリセットされる()
    {
        var g = new RecenterGesture();
        Assert.False(g.Update(true, true, true, true, 0.9f));    // Holding, t=0
        Assert.False(g.Update(true, true, true, true, 0.9f));    // t=0.9
        Assert.False(g.Update(false, false, false, false, 0.1f)); // 両手離す → Idle/リセット
        Assert.False(g.Update(true, true, true, true, 0.9f));    // 再 Holding, t=0
        Assert.False(g.Update(true, true, true, true, 0.05f));   // t=0.05 < 1.0（リセット済の証拠）
    }

    [Fact]
    public void fire後に両手を離せば再武装して再発火する()
    {
        var g = new RecenterGesture();
        g.Update(true, true, true, true, 1.0f);
        Assert.True(g.Update(true, true, true, true, 1.0f));     // fire1
        Assert.False(g.Update(false, false, false, false, 0.1f)); // 両手離す → 再武装
        Assert.False(g.Update(true, true, true, true, 1.0f));    // 再 Holding
        Assert.True(g.Update(true, true, true, true, 1.0f));     // fire2
    }

    [Fact]
    public void fire後に片手だけ離しても再武装しない()
    {
        var g = new RecenterGesture();
        g.Update(true, true, true, true, 1.0f);
        Assert.True(g.Update(true, true, true, true, 1.0f));   // fire
        Assert.False(g.Update(true, false, true, true, 0.1f)); // 左だけ離す（右保持）→ Fired のまま
        Assert.False(g.Update(true, true, true, true, 1.0f));  // 再 grip しても Fired のまま
        Assert.False(g.Update(true, true, true, true, 1.0f));  // 発火しない
    }

    [Fact]
    public void Clearで初期状態へ戻る()
    {
        var g = new RecenterGesture();
        g.Update(true, true, true, true, 1.0f);
        g.Clear();
        Assert.False(g.Update(true, true, true, true, 0.5f)); // Idle→Holding, t=0
    }
}
