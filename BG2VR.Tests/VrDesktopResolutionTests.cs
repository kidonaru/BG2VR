using BG2VR.DesktopLowRes;
using Xunit;

namespace BG2VR.Tests;

public class VrDesktopResolutionTests
{
    // 既定/代表値の正確性。高さは width×9/16 を 4px 単位に丸めた値。
    [Theory]
    [InlineData(480, 480, 272)]    // 270 → 4px snap → 272
    [InlineData(640, 640, 360)]    // 360（4 の倍数, そのまま）
    [InlineData(800, 800, 452)]    // 450 → 452
    [InlineData(1280, 1280, 720)]  // 既定（720）
    [InlineData(1600, 1600, 900)]  // 900
    [InlineData(1920, 1920, 1080)] // 1080
    public void Derive_returns_expected(int width, int expW, int expH)
    {
        var (w, h) = VrDesktopResolution.Derive(width);
        Assert.Equal(expW, w);
        Assert.Equal(expH, h);
    }

    // 高さは常に 4 の倍数（奇数/半端を避ける不変条件）。
    [Theory]
    [InlineData(480)]
    [InlineData(960)]
    [InlineData(1136)]
    [InlineData(1280)]
    [InlineData(1920)]
    public void Height_is_multiple_of_4(int width)
    {
        var (_, h) = VrDesktopResolution.Derive(width);
        Assert.Equal(0, h % 4);
    }

    // 全幅で aspect が 16:9 ±0.05 内（GBSystem.Update の再アサート許容に収まる＝flap しない）。
    [Fact]
    public void Aspect_stays_within_game_tolerance()
    {
        const double target = 16.0 / 9.0; // 1.7777...
        for (int width = 480; width <= 1920; width += 16)
        {
            var (w, h) = VrDesktopResolution.Derive(width);
            double aspect = (double)w / h;
            Assert.True(System.Math.Abs(aspect - target) < 0.05,
                $"width={width} → {w}x{h} aspect={aspect:F4} が 16:9±0.05 を外れた");
        }
    }

    // 退行入力（0/負/極小）は幅 16 にクランプされ、高さ下限 4 を割らない（クランプ値と方向を固定）。
    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(int.MinValue)]
    public void Degenerate_width_clamps_to_16x8(int width)
    {
        var (w, h) = VrDesktopResolution.Derive(width);
        Assert.Equal(16, w);
        Assert.Equal(8, h);
        Assert.True(h >= 4);
    }

    // 奇数幅（手編集 .cfg）は偶数に snap される（ドライバ幅丸めによる幅照合 flap を排除）。
    [Theory]
    [InlineData(481, 480)]
    [InlineData(1281, 1280)]
    [InlineData(1919, 1918)]
    public void Odd_width_snaps_to_even(int width, int expW)
    {
        var (w, _) = VrDesktopResolution.Derive(width);
        Assert.Equal(expW, w);
        Assert.Equal(0, w % 2);
    }
}
