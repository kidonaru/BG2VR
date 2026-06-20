using BG2VR.VrInput;
using Xunit;

namespace BG2VR.Tests;

public class ReticlePainterTests
{
    // 中心ドットは不透明（α=255）。
    [Fact]
    public void Center_dot_is_opaque()
    {
        var px = ReticlePainter.Paint();
        Assert.Equal(255, px[31 * ReticlePainter.Size + 31].a);
    }

    // リング帯（r≈0.78・中心線上）は不透明。
    [Fact]
    public void Ring_band_is_opaque()
    {
        var px = ReticlePainter.Paint();
        // (56,31): dx=24.5, dy=0.5 → r≈0.778（リング中心線 0.78 のほぼ真上）
        Assert.Equal(255, px[31 * ReticlePainter.Size + 56].a);
    }

    // ドットとリングの間（r≈0.43）は完全透明。
    [Fact]
    public void Gap_between_dot_and_ring_is_transparent()
    {
        var px = ReticlePainter.Paint();
        // (45,31): dx=13.5, dy=0.5 → r≈0.429
        Assert.Equal(0, px[31 * ReticlePainter.Size + 45].a);
    }

    // 四隅（r>1）は完全透明（quad の縁が見えない）。
    [Fact]
    public void Corners_are_transparent()
    {
        var px = ReticlePainter.Paint();
        Assert.Equal(0, px[0].a);
        Assert.Equal(0, px[ReticlePainter.Size * ReticlePainter.Size - 1].a);
    }

    // RGB は全 pixel 白（色は material.color で着色する設計）。
    [Fact]
    public void All_pixels_are_white_rgb()
    {
        var px = ReticlePainter.Paint();
        foreach (var p in px)
        {
            Assert.Equal(255, p.r);
            Assert.Equal(255, p.g);
            Assert.Equal(255, p.b);
        }
    }
}
