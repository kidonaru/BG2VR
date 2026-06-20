using BG2VR.VrInput;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests;

public class QuadRaycasterTests
{
    // 正面中央を撃つ → RT 中心 pixel。
    [Fact]
    public void Center_hit_maps_to_center_pixel()
    {
        var h = QuadRaycaster.Raycast(
            new Vector3(0, 0, -1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
            0.5f, 0.5f, 1920, 1080);
        Assert.True(h.Valid);
        Assert.Equal(960f, h.Pixel.x, 2);
        Assert.Equal(540f, h.Pixel.y, 2);
    }

    // +X 方向にずらして撃つ → pixel.x が右へ比例移動。
    [Fact]
    public void Offset_hit_maps_proportionally()
    {
        var h = QuadRaycaster.Raycast(
            new Vector3(0.25f, 0, -1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
            0.5f, 0.5f, 1920, 1080);
        Assert.True(h.Valid);
        Assert.Equal(1440f, h.Pixel.x, 2); // u=0.75
        Assert.Equal(540f, h.Pixel.y, 2);
    }

    // 矩形外 → miss。
    [Fact]
    public void Outside_quad_misses()
    {
        var h = QuadRaycaster.Raycast(
            new Vector3(0.6f, 0, -1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
            0.5f, 0.5f, 1920, 1080);
        Assert.False(h.Valid);
    }

    // 平面に平行 → miss。
    [Fact]
    public void Parallel_ray_misses()
    {
        var h = QuadRaycaster.Raycast(
            new Vector3(0, 0, -1), new Vector3(1, 0, 0),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
            0.5f, 0.5f, 1920, 1080);
        Assert.False(h.Valid);
    }

    // 後方（t<0） → miss。
    [Fact]
    public void Behind_ray_misses()
    {
        var h = QuadRaycaster.Raycast(
            new Vector3(0, 0, 1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
            0.5f, 0.5f, 1920, 1080);
        Assert.False(h.Valid);
    }

    // 実寸（lossyScale 乗算済み）半幅を渡すと、実幅比率の位置が pixel に正しく写る
    //（呼び出し側契約・WorldScale≠1 のずれ修正の根拠）。
    [Fact]
    public void Scaled_extents_map_pixel_by_actual_fraction()
    {
        // rig scale 0.855 相当: 公称半幅 0.5 → 実半幅 0.4275。実半幅の 50% の位置を撃つ → u=0.75。
        float s = 0.855f;
        var h = QuadRaycaster.Raycast(
            new Vector3(0.5f * 0.5f * s, 0, -1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
            0.5f * s, 0.5f * s, 1920, 1080);
        Assert.True(h.Valid);
        Assert.Equal(1440f, h.Pixel.x, 2);
        Assert.Equal(540f, h.Pixel.y, 2);
    }

    // Normal は ray 始点側を向く（quadNormal の符号に依らない＝符号不感設計の維持）。
    [Fact]
    public void Normal_faces_ray_origin_regardless_of_quad_normal_sign()
    {
        var a = QuadRaycaster.Raycast(
            new Vector3(0, 0, -1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, Vector3.forward, 0.5f, 0.5f, 1, 1);
        var b = QuadRaycaster.Raycast(
            new Vector3(0, 0, -1), new Vector3(0, 0, 1),
            Vector3.zero, Vector3.right, Vector3.up, -Vector3.forward, 0.5f, 0.5f, 1, 1);
        Assert.True(a.Valid);
        Assert.True(b.Valid);
        Assert.Equal(-1f, a.Normal.z, 3); // どちらも視点側（-Z）を向く
        Assert.Equal(-1f, b.Normal.z, 3);
    }
}
