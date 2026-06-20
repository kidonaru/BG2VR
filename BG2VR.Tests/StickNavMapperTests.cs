using BG2VR.VrInput;
using UnityEngine;
using Xunit;

public class StickNavMapperTests
{
    // テスト用しきい値。yaml 既定値とは意図的に独立させる（既定値は実機チューニングで変わるため・CLAUDE.md 規約）
    private const float Th = 0.5f;

    [Fact]
    public void engage_で_Held_と_Pulse_が立つ()
    {
        var m = new StickNavMapper();
        var s = m.Update(new Vector2(0f, 0.6f), Th);
        Assert.True(s.UpHeld);
        Assert.True(s.UpPulse);
        Assert.False(s.DownHeld);
        Assert.False(s.LeftHeld);
        Assert.False(s.RightHeld);
    }

    [Fact]
    public void Pulse_は立ち上がりの_1_フレームのみ()
    {
        var m = new StickNavMapper();
        m.Update(new Vector2(0f, 0.6f), Th);
        var s = m.Update(new Vector2(0f, 0.6f), Th);
        Assert.True(s.UpHeld);
        Assert.False(s.UpPulse);
    }

    [Fact]
    public void ヒステリシス_release_と_engage_の間では_Held_維持()
    {
        var m = new StickNavMapper();
        m.Update(new Vector2(0f, 0.6f), Th);
        var s = m.Update(new Vector2(0f, 0.45f), Th); // release=0.4 < 0.45 < engage=0.5
        Assert.True(s.UpHeld);
        Assert.False(s.UpPulse);
        s = m.Update(new Vector2(0f, 0.35f), Th); // release 下回り → 解除
        Assert.False(s.UpHeld);
    }

    [Fact]
    public void 解除後の再_engage_で再び_Pulse()
    {
        var m = new StickNavMapper();
        m.Update(new Vector2(0f, 0.6f), Th);
        m.Update(Vector2.zero, Th);
        var s = m.Update(new Vector2(0f, 0.6f), Th);
        Assert.True(s.UpPulse);
    }

    [Fact]
    public void engage_未満では何も立たない()
    {
        var m = new StickNavMapper();
        var s = m.Update(new Vector2(0f, 0.45f), Th);
        Assert.False(s.UpHeld);
        Assert.False(s.UpPulse);
    }

    [Fact]
    public void しきい値引数が反映される()
    {
        var m = new StickNavMapper();
        var s = m.Update(new Vector2(0f, 0.45f), 0.4f);
        Assert.True(s.UpHeld);
    }

    [Fact]
    public void 斜めは両方向同時_Held()
    {
        var m = new StickNavMapper();
        var s = m.Update(new Vector2(0.6f, 0.6f), Th);
        Assert.True(s.UpHeld);
        Assert.True(s.RightHeld);
        Assert.True(s.UpPulse);
        Assert.True(s.RightPulse);
    }

    [Fact]
    public void 負方向は_Down_Left_に対応()
    {
        var m = new StickNavMapper();
        var s = m.Update(new Vector2(-0.6f, -0.6f), Th);
        Assert.True(s.DownHeld);
        Assert.True(s.LeftHeld);
        Assert.False(s.UpHeld);
        Assert.False(s.RightHeld);
    }

    [Fact]
    public void 符号反転は旧方向解除_新方向_Pulse()
    {
        var m = new StickNavMapper();
        m.Update(new Vector2(0f, 0.6f), Th);
        var s = m.Update(new Vector2(0f, -0.6f), Th);
        Assert.False(s.UpHeld);
        Assert.True(s.DownHeld);
        Assert.True(s.DownPulse);
    }

    [Fact]
    public void Reset_で状態が消える()
    {
        var m = new StickNavMapper();
        m.Update(new Vector2(0f, 0.6f), Th);
        m.Reset();
        var s = m.Update(new Vector2(0f, 0.6f), Th);
        Assert.True(s.UpPulse); // reset 後は再び立ち上がり扱い
    }
}
