using BG2VR.VrInput;
using Xunit;

namespace BG2VR.Tests;

public class NavRepeatTests
{
    // 既定相当の値（テスト fixture は yaml 既定から独立・CLAUDE.md）。
    private const float Delay = 0.4f;
    private const float Interval = 0.12f;

    [Fact]
    public void 立ち上がりで即時1発()
    {
        var r = new NavRepeat();
        Assert.True(r.Update(true, Delay, Interval, 0.016f));
    }

    [Fact]
    public void held無しは無発火()
    {
        var r = new NavRepeat();
        Assert.False(r.Update(false, Delay, Interval, 0.016f));
    }

    [Fact]
    public void 初動delay経過まで2発目は出ない()
    {
        var r = new NavRepeat();
        Assert.True(r.Update(true, Delay, Interval, 0f)); // 即時
        // delay 未満の累積では発火しない
        Assert.False(r.Update(true, Delay, Interval, 0.2f));
        Assert.False(r.Update(true, Delay, Interval, 0.15f)); // 累積 0.35 < 0.4
    }

    [Fact]
    public void delay経過で2発目その後はintervalごと連射()
    {
        var r = new NavRepeat();
        Assert.True(r.Update(true, Delay, Interval, 0f)); // 即時(1発目)
        Assert.False(r.Update(true, Delay, Interval, 0.3f));  // 0.3 < delay
        Assert.True(r.Update(true, Delay, Interval, 0.15f));  // 累積 0.45 >= delay → 2発目
        // 連射フェーズ: interval ごと
        Assert.False(r.Update(true, Delay, Interval, 0.05f)); // 0.05 < interval
        Assert.True(r.Update(true, Delay, Interval, 0.08f));  // 累積 0.13 >= interval → 3発目
    }

    [Fact]
    public void release後は連射状態がリセットされ再度立ち上がりで即時()
    {
        var r = new NavRepeat();
        r.Update(true, Delay, Interval, 0f);
        r.Update(true, Delay, Interval, 0.5f); // 連射フェーズへ
        Assert.False(r.Update(false, Delay, Interval, 0.016f)); // release
        // 再度 held: 立ち上がり即時
        Assert.True(r.Update(true, Delay, Interval, 0.016f));
        // 直後は delay 計測（連射フェーズに戻らない）
        Assert.False(r.Update(true, Delay, Interval, 0.2f));
    }

    [Fact]
    public void Resetで状態が消える()
    {
        var r = new NavRepeat();
        r.Update(true, Delay, Interval, 0f);
        r.Reset();
        // Reset 後は held=true で再度「立ち上がり」扱い＝即時
        Assert.True(r.Update(true, Delay, Interval, 0.016f));
    }
}
