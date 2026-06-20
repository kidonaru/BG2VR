using BG2VR.VrInput;
using Xunit;

public class PointerHandArbiterTests
{
    private const float Onset = 0.05f;

    [Fact]
    public void 初期はポインタ右()
    {
        var a = new PointerHandArbiter();
        Assert.False(a.PointerIsLeft);
    }

    [Fact]
    public void 左トリガーonsetのrisingで左へ切替()
    {
        var a = new PointerHandArbiter();
        bool switched = a.Update(true, 0.1f, true, 0f, Onset);
        Assert.True(switched);
        Assert.True(a.PointerIsLeft);
    }

    [Fact]
    public void 押しっぱなし継続では切替えない_risingのみ()
    {
        var a = new PointerHandArbiter();
        a.Update(true, 0.1f, true, 0f, Onset);                  // 左へ切替
        bool switched = a.Update(true, 0.1f, true, 0f, Onset);  // 保持
        Assert.False(switched);
        Assert.True(a.PointerIsLeft);
    }

    [Fact]
    public void ポインタ手自身のonsetでは何も起きない()
    {
        var a = new PointerHandArbiter();
        bool switched = a.Update(false, 0f, true, 0.5f, Onset); // 右=現ポインタ手の rising
        Assert.False(switched);
        Assert.False(a.PointerIsLeft);
    }

    [Fact]
    public void 両手同時risingは現状維持()
    {
        var a = new PointerHandArbiter();
        bool switched = a.Update(true, 0.5f, true, 0.5f, Onset);
        Assert.False(switched);
        Assert.False(a.PointerIsLeft);
    }

    [Fact]
    public void invalid手のonsetは無視()
    {
        var a = new PointerHandArbiter();
        bool switched = a.Update(false, 1f, true, 0f, Onset); // 左 invalid + トリガー全押し
        Assert.False(switched);
        Assert.False(a.PointerIsLeft);
    }

    [Fact]
    public void invalid中の押下はvalid復帰フレームでrising扱い()
    {
        var a = new PointerHandArbiter();
        a.Update(false, 1f, true, 0f, Onset);                   // 左 invalid 中の押下=無視
        bool switched = a.Update(true, 1f, true, 0f, Onset);    // valid 復帰 → rising
        Assert.True(switched);
        Assert.True(a.PointerIsLeft);
    }

    [Fact]
    public void 左右を行き来できる()
    {
        var a = new PointerHandArbiter();
        a.Update(true, 0.5f, true, 0f, Onset);   // 左へ
        a.Update(true, 0f, true, 0f, Onset);     // 両離し
        bool switched = a.Update(true, 0f, true, 0.5f, Onset); // 右 rising
        Assert.True(switched);
        Assert.False(a.PointerIsLeft);
    }

    [Fact]
    public void Resetで初期状態に戻る()
    {
        var a = new PointerHandArbiter();
        a.Update(true, 0.5f, true, 0f, Onset);   // 左へ
        a.Reset();
        Assert.False(a.PointerIsLeft);
    }
}
