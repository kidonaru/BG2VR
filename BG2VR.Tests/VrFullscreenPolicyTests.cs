using BG2VR.DesktopLowRes;
using Xunit;

namespace BG2VR.Tests;

public class VrFullscreenPolicyTests
{
    // 真理値表（want, forced, currentWindowed → action）。
    [Theory]
    // VR on + 機能 ON、未強制、windowed → フルスクリーン化
    [InlineData(true, false, true, VrFullscreenAction.ForceFullscreen)]
    // VR on、未強制、既にフルスクリーン → 何もしない（forced を立てない）
    [InlineData(true, false, false, VrFullscreenAction.None)]
    // VR on、強制済み → 維持（windowed の値に関わらず None）
    [InlineData(true, true, true, VrFullscreenAction.None)]
    [InlineData(true, true, false, VrFullscreenAction.None)]
    // VR off、強制済み → 復元
    [InlineData(false, true, true, VrFullscreenAction.Restore)]
    [InlineData(false, true, false, VrFullscreenAction.Restore)]
    // VR off、未強制 → 何もしない（自分で変えていないので触らない）
    [InlineData(false, false, true, VrFullscreenAction.None)]
    [InlineData(false, false, false, VrFullscreenAction.None)]
    public void Decide_returns_expected(bool want, bool forced, bool currentWindowed, VrFullscreenAction expected)
    {
        Assert.Equal(expected, VrFullscreenPolicy.Decide(want, forced, currentWindowed));
    }
}
