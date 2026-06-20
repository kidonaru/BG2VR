using Xunit;
using BG2VR.VrInput;

public class FingerCurlMathTests
{
    [Fact]
    public void Smooth_tauゼロ以下はパススルー()
    {
        Assert.Equal(1f, FingerCurlMath.Smooth(0f, 1f, 0.016f, 0f), 4);
        Assert.Equal(1f, FingerCurlMath.Smooth(0f, 1f, 0.016f, -1f), 4);
    }

    [Fact]
    public void Smooth_prevとtargetが同じなら不変()
    {
        Assert.Equal(0.3f, FingerCurlMath.Smooth(0.3f, 0.3f, 0.016f, 0.05f), 4);
    }

    [Fact]
    public void Smooth_dtがtauと等しいとき約0_632進む()
    {
        // alpha = 1 - e^-1 ≈ 0.63212
        float r = FingerCurlMath.Smooth(0f, 1f, 0.05f, 0.05f);
        Assert.True(System.Math.Abs(r - 0.63212f) < 1e-3f);
    }

    [Fact]
    public void Smooth_単調にtargetへ近づき越えない()
    {
        float v = 0f;
        for (int i = 0; i < 200; i++)
        {
            float prev = v;
            v = FingerCurlMath.Smooth(v, 1f, 0.016f, 0.05f);
            Assert.True(v >= prev);          // 単調増加
            Assert.True(v <= 1.0001f);       // target を越えない
        }
        Assert.True(v > 0.99f);              // 収束
    }

    [Fact]
    public void CurlAngle_初期0なら両端と中間()
    {
        Assert.Equal(0f, FingerCurlMath.CurlAngle(0f, 0f, 55f), 4);
        Assert.Equal(55f, FingerCurlMath.CurlAngle(1f, 0f, 55f), 4);
        Assert.Equal(27.5f, FingerCurlMath.CurlAngle(0.5f, 0f, 55f), 4);
    }

    [Fact]
    public void CurlAngle_範囲外はクランプ()
    {
        Assert.Equal(55f, FingerCurlMath.CurlAngle(2f, 0f, 55f), 4);   // >1 → 1
        Assert.Equal(0f, FingerCurlMath.CurlAngle(-1f, 0f, 55f), 4);   // <0 → 0
    }

    [Fact]
    public void CurlAngle_初期角度から最大角へ補間()
    {
        // 入力0=初期角・入力1=最大角・中間は線形（remap）
        Assert.Equal(10f, FingerCurlMath.CurlAngle(0f, 10f, 55f), 4);     // 入力0 → 初期
        Assert.Equal(55f, FingerCurlMath.CurlAngle(1f, 10f, 55f), 4);     // 入力1 → 最大
        Assert.Equal(32.5f, FingerCurlMath.CurlAngle(0.5f, 10f, 55f), 4); // 中間 = (10+55)/2
        Assert.Equal(10f, FingerCurlMath.CurlAngle(-1f, 10f, 55f), 4);    // クランプで初期維持
    }
}
