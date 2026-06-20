using BG2VR.VrFade;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests;

public class OverlayUvMapperTests
{
    // 単独 texture（rect=全面・flip なし）→ 恒等 UV。
    [Fact]
    public void Full_rect_no_flip_is_identity()
    {
        var uv = OverlayUvMapper.Map(new Rect(0, 0, 1920, 1080), 1920, 1080, flipV: false);
        Assert.Equal(0f, uv.UMin);
        Assert.Equal(0f, uv.VMin);
        Assert.Equal(1f, uv.UMax);
        Assert.Equal(1f, uv.VMax);
    }

    // D3D11 の V 反転: 全面 rect は {vMin=1, vMax=0}（SteamVR 慣例の flip 表現）。
    [Fact]
    public void Full_rect_flip_swaps_v()
    {
        var uv = OverlayUvMapper.Map(new Rect(0, 0, 1920, 1080), 1920, 1080, flipV: true);
        Assert.Equal(1f, uv.VMin);
        Assert.Equal(0f, uv.VMax);
    }

    // atlas 部分矩形: texture サイズで正規化 + flip で V が 1-v に写る。
    [Fact]
    public void Atlas_subrect_normalizes_and_flips()
    {
        // 2048x2048 atlas の左下 512px 帯 (x:0..1024, y:0..512)
        var uv = OverlayUvMapper.Map(new Rect(0, 0, 1024, 512), 2048, 2048, flipV: true);
        Assert.Equal(0f, uv.UMin);
        Assert.Equal(0.5f, uv.UMax);
        Assert.Equal(1f, uv.VMin);    // 1 - 0/2048
        Assert.Equal(0.75f, uv.VMax); // 1 - 512/2048
    }

    // 退化入力（texture サイズ 0 = 未確定）→ 全面 UV にフォールバック。
    [Fact]
    public void Degenerate_size_falls_back_to_full()
    {
        var uv = OverlayUvMapper.Map(new Rect(0, 0, 4, 4), 0, 0, flipV: false);
        Assert.Equal(0f, uv.UMin);
        Assert.Equal(1f, uv.UMax);
        Assert.Equal(0f, uv.VMin);
        Assert.Equal(1f, uv.VMax);
    }

    // 片側のみ 0（height=0）でもフォールバックする（OR 判定）。
    [Fact]
    public void Degenerate_height_only_falls_back_to_full()
    {
        var uv = OverlayUvMapper.Map(new Rect(0, 0, 4, 4), 1920, 0, flipV: false);
        Assert.Equal(0f, uv.UMin);
        Assert.Equal(1f, uv.UMax);
        Assert.Equal(0f, uv.VMin);
        Assert.Equal(1f, uv.VMax);
    }

    // 退化入力 + flip → 全面 flip UV（{vMin=1, vMax=0}）にフォールバック。
    [Fact]
    public void Degenerate_size_with_flip_falls_back_to_flipped_full()
    {
        var uv = OverlayUvMapper.Map(new Rect(0, 0, 4, 4), 0, 0, flipV: true);
        Assert.Equal(0f, uv.UMin);
        Assert.Equal(1f, uv.UMax);
        Assert.Equal(1f, uv.VMin);
        Assert.Equal(0f, uv.VMax);
    }
}
