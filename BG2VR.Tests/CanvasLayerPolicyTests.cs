using Xunit;
using BG2VR.WorldUi;

public class CanvasLayerPolicyTests
{
    [Fact]
    public void Default_is_mapped_to_UI()
    {
        Assert.Equal(5, CanvasLayerPolicy.EffectiveLayer(0));
    }

    [Fact]
    public void UI_layer_is_unchanged()
    {
        Assert.Equal(5, CanvasLayerPolicy.EffectiveLayer(5));
    }

    [Fact]
    public void Other_layer_is_unchanged()
    {
        Assert.Equal(8, CanvasLayerPolicy.EffectiveLayer(8));
    }
}
