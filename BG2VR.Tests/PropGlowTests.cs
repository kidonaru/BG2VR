using Xunit;
using UnityEngine;
using BG2VR.VrInput;

// PropGlow 純関数（彩度ベースの発光判定 + HDR emission 生成）のテスト。
// fixture はサイリウム/タンバリンの実測色（spec §3.2/§3.3・2026-06-20 BG2DevBridge 採取）。yaml 既定値からは独立。
public class PropGlowTests
{
    // しきい値はテスト内で固定（yaml 既定 0.35 と同値だが意図的に独立定数として持つ＝既定変更で stale 化しない）。
    private const float Thr = 0.35f;

    // --- Saturation ---

    [Fact]
    public void Saturation_PureBlack_IsZero_NoDivideByZero()
    {
        // 0 除算ガードの回帰固定（黒入力でしか検出できない）。max≈0 → NaN でなく 0。
        Assert.Equal(0f, PropGlow.Saturation(new Color(0f, 0f, 0f)), 4);
    }

    [Fact]
    public void Saturation_Tube_IsHigh()
    {
        // (1.0,0.22,0.70) → (1-0.22)/1 = 0.78
        Assert.Equal(0.78f, PropGlow.Saturation(new Color(1.0f, 0.22f, 0.70f)), 2);
    }

    [Fact]
    public void Saturation_Gray_IsNearZero()
    {
        // グレー（max≈min）は彩度ほぼ 0。
        Assert.True(PropGlow.Saturation(new Color(0.77f, 0.77f, 0.77f)) < 0.01f);
    }

    // --- IsGlowing（サイリウム submesh）---

    [Fact]
    public void IsGlowing_Tube_True()
    {
        // 鮮ピンク Tube(sat 0.78) は発光部。
        Assert.True(PropGlow.IsGlowing(new Color(1.0f, 0.22f, 0.70f), Thr));
    }

    [Fact]
    public void IsGlowing_Core_False()
    {
        // 薄ピンク Core(sat 0.23) は既定しきい値では非発光。
        Assert.False(PropGlow.IsGlowing(new Color(1.0f, 0.77f, 0.91f), Thr));
    }

    [Theory]
    [InlineData(0.80f, 0.80f, 0.81f)] // Button
    [InlineData(0.77f, 0.77f, 0.77f)] // Handle
    [InlineData(0.91f, 0.91f, 0.92f)] // Ring
    public void IsGlowing_GrayParts_False(float r, float g, float b)
    {
        Assert.False(PropGlow.IsGlowing(new Color(r, g, b), Thr));
    }

    [Fact]
    public void IsGlowing_PureBlack_False()
    {
        Assert.False(PropGlow.IsGlowing(new Color(0f, 0f, 0f), Thr));
    }

    // --- タンバリン金/茶（設計意図の固定）---
    // 彩度判定をプロップ共通に掛けるとタンバリンの金/茶も発光判定が立つ＝だから runner で
    // kind==GlowStick に scope するのが必須、という設計意図をテストで固定する（spec §3.3）。

    [Theory]
    [InlineData(0.89f, 0.72f, 0.35f)] // 金 sat≈0.61
    [InlineData(0.77f, 0.51f, 0.25f)] // 茶 sat≈0.68
    public void IsGlowing_TambourineGoldBrown_True_HenceMustScopeToGlowStick(float r, float g, float b)
    {
        Assert.True(PropGlow.IsGlowing(new Color(r, g, b), Thr));
    }

    // --- EmissionColor ---

    [Fact]
    public void EmissionColor_ScalesRgbByStrength_AlphaOne()
    {
        Color e = PropGlow.EmissionColor(new Color(1.0f, 0.22f, 0.70f), 2.5f);
        Assert.Equal(2.5f, e.r, 3);
        Assert.Equal(0.55f, e.g, 3);
        Assert.Equal(1.75f, e.b, 3);
        Assert.Equal(1f, e.a, 3); // alpha は常に 1
    }

    [Fact]
    public void EmissionColor_ZeroStrength_IsBlack()
    {
        Color e = PropGlow.EmissionColor(new Color(1.0f, 0.22f, 0.70f), 0f);
        Assert.Equal(0f, e.r, 3);
        Assert.Equal(0f, e.g, 3);
        Assert.Equal(0f, e.b, 3);
    }
}
