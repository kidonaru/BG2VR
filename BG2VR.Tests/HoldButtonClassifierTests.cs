using BG2VR.VrInput;
using Xunit;

public class HoldButtonClassifierTests
{
    private const float Dt = 1f / 60f;

    [Fact]
    public void 短押しはリリースフレームで発火()
    {
        var c = new HoldButtonClassifier();
        var r = c.Update(true, Dt);
        Assert.True(r.JustPressed);
        Assert.False(r.ShortPress);
        r = c.Update(true, 0.3f); // 閾値未満の保持
        Assert.False(r.ShortPress);
        Assert.False(r.LongPress);
        r = c.Update(false, Dt);  // リリース
        Assert.True(r.ShortPress);
        Assert.False(r.LongPress);
    }

    [Fact]
    public void 長押しは閾値到達フレームで1回だけ発火()
    {
        var c = new HoldButtonClassifier();
        c.Update(true, Dt);
        var r = c.Update(true, HoldButtonClassifier.LongPressSeconds); // 閾値到達
        Assert.True(r.LongPress);
        r = c.Update(true, 1f); // 押し続けても再発火しない
        Assert.False(r.LongPress);
        r = c.Update(false, Dt); // 長押し後のリリースで ShortPress は出ない
        Assert.False(r.ShortPress);
    }

    [Fact]
    public void ConsumePressでその押下はリリースまで何も出ない()
    {
        var c = new HoldButtonClassifier();
        c.Update(true, Dt);
        c.ConsumePress();
        var r = c.Update(true, HoldButtonClassifier.LongPressSeconds); // 閾値到達しても
        Assert.False(r.LongPress);
        r = c.Update(false, Dt); // リリースしても
        Assert.False(r.ShortPress);
        // 次の押下は通常分類に戻る
        r = c.Update(true, Dt);
        Assert.True(r.JustPressed);
        r = c.Update(false, Dt);
        Assert.True(r.ShortPress);
    }

    [Fact]
    public void 未押下時のConsumePressはnoop()
    {
        var c = new HoldButtonClassifier();
        c.ConsumePress(); // 押していない
        c.Update(true, Dt);
        var r = c.Update(false, Dt);
        Assert.True(r.ShortPress); // 影響なし
    }

    [Fact]
    public void Resetで押下追跡が消える()
    {
        var c = new HoldButtonClassifier();
        c.Update(true, Dt);
        c.Reset();
        var r = c.Update(false, Dt); // リリースに見えるが prev が消えている
        Assert.False(r.ShortPress);
    }
}
