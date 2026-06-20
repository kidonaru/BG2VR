using BG2VR.WorldUi;
using Xunit;

namespace BG2VR.Tests;

public class CanvasRootClassifierTests
{
    [Theory]
    // isLive, isActive, isOverlay, hasRaycaster → expected
    [InlineData(true,  true,  true,  true,  true)]   // #5 / シーン Canvas
    [InlineData(false, true,  true,  true,  false)]  // prefab（非 live）
    [InlineData(true,  false, true,  true,  false)]  // 非 active（Debug 親 inactive 等）
    [InlineData(true,  true,  false, true,  false)]  // WorldSpace（isOverlay=false・HomeScene キャラ）
    [InlineData(true,  true,  true,  false, false)]  // ScreenFade/BGFade（raycaster 無し）
    public void IsProjectableRoot_matches_survey(bool isLive, bool isActive, bool isOverlay, bool ray, bool expected)
    {
        Assert.Equal(expected, CanvasRootClassifier.IsProjectableRoot(isLive, isActive, isOverlay, ray));
    }
}
