using Xunit;
using BG2VR.VrInput;

public class ChekiZoomTests
{
    [Fact]
    public void Step_StickUp_DecreasesFov_ZoomIn()
    {
        // stickY>0 = ズームイン = FOV 減少。speed=30°/s, dt=0.1 → -3°
        Assert.Equal(47f, ChekiZoom.Step(50f, 1f, 30f, 0.1f, 20f, 60f), 3);
    }

    [Fact]
    public void Step_StickDown_IncreasesFov_ZoomOut()
    {
        Assert.Equal(53f, ChekiZoom.Step(50f, -1f, 30f, 0.1f, 20f, 60f), 3);
    }

    [Fact]
    public void Step_ClampsToMin()
    {
        Assert.Equal(20f, ChekiZoom.Step(21f, 1f, 30f, 1f, 20f, 60f), 3); // -30 → clamp 20
    }

    [Fact]
    public void Step_ClampsToMax()
    {
        Assert.Equal(60f, ChekiZoom.Step(59f, -1f, 30f, 1f, 20f, 60f), 3); // +30 → clamp 60
    }
}
