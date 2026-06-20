using Xunit;
using BG2VR.VrInput;

public class VrCameraMaskTests
{
    [Fact]
    public void Exclude_RemovesSingleLayerBit()
    {
        int result = VrCameraMask.Exclude(~0, 30);
        Assert.Equal(0, result & (1 << 30));      // 層30 が落ちている
        Assert.Equal(1 << 5, result & (1 << 5));  // 他の層は残る
    }

    [Fact]
    public void Exclude_RemovesMultipleLayers()
    {
        int result = VrCameraMask.Exclude(~0, 30, 5);
        Assert.Equal(0, result & (1 << 30));
        Assert.Equal(0, result & (1 << 5));
    }

    [Fact]
    public void Exclude_NoLayers_ReturnsBaseUnchanged()
    {
        Assert.Equal(0x00ABCDEF, VrCameraMask.Exclude(0x00ABCDEF));
    }
}
