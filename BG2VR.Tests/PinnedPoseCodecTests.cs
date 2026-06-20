using System.Collections.Generic;
using System.Threading;
using System.Globalization;
using UnityEngine;
using Xunit;
using BG2VR.ScenePinned;

public class PinnedPoseCodecTests
{
    [Fact]
    public void 往復で位置とyawが一致する()
    {
        var src = new Dictionary<string, PinnedPose>
        {
            ["HoleScene"] = new PinnedPose(new Vector3(1.5f, 1.2f, -2.25f), 180f),
            ["Talk2DScene"] = new PinnedPose(new Vector3(-0.1f, 0f, 3.7f), -45.5f),
        };
        var round = PinnedPoseCodec.Parse(PinnedPoseCodec.Serialize(src));
        Assert.Equal(2, round.Count);
        Assert.True((round["HoleScene"].Position - new Vector3(1.5f, 1.2f, -2.25f)).magnitude < 1e-4f);
        Assert.Equal(180f, round["HoleScene"].Yaw, 3);
        Assert.True((round["Talk2DScene"].Position - new Vector3(-0.1f, 0f, 3.7f)).magnitude < 1e-4f);
        Assert.Equal(-45.5f, round["Talk2DScene"].Yaw, 3);
    }

    [Fact]
    public void Serializeはinvariant_cultureでコンマ小数を出さない()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // 小数点がコンマの culture
        try
        {
            var src = new Dictionary<string, PinnedPose> { ["X"] = new PinnedPose(new Vector3(1.5f, 0f, 0f), 0f) };
            string json = PinnedPoseCodec.Serialize(src);
            Assert.Contains("\"x\": 1.5", json);
            Assert.DoesNotContain("1,5", json);
        }
        finally { Thread.CurrentThread.CurrentCulture = prev; }
    }

    [Fact]
    public void 空文字やnullやゴミは空辞書を返す()
    {
        Assert.Empty(PinnedPoseCodec.Parse(null));
        Assert.Empty(PinnedPoseCodec.Parse(""));
        Assert.Empty(PinnedPoseCodec.Parse("not json at all"));
        Assert.Empty(PinnedPoseCodec.Parse("{\"version\":1,\"poses\":{}}"));
    }

    [Fact]
    public void near_zero成分_負の指数表記_yaw0も往復する()
    {
        // float.ToString("R") は near-zero 値を負の指数（例 1E-08 / -2.5E-06）で書く。
        // 旧 regex は E 直後の '-' で停止しエントリ脱落＝保存 pose の静かな消失だった。回帰固定。
        var src = new Dictionary<string, PinnedPose>
        {
            ["Origin"] = new PinnedPose(new Vector3(1e-08f, -2.5e-06f, 0f), 0f),
        };
        var round = PinnedPoseCodec.Parse(PinnedPoseCodec.Serialize(src));
        Assert.True(round.ContainsKey("Origin"));
        var p = round["Origin"].Position;
        Assert.True(Mathf.Abs(p.x - 1e-08f) < 1e-10f);
        Assert.True(Mathf.Abs(p.y - (-2.5e-06f)) < 1e-10f);
        Assert.Equal(0f, p.z, 6);
        Assert.Equal(0f, round["Origin"].Yaw, 6);
    }

    [Fact]
    public void 整形JSONは改行とインデントを含みそのまま往復する()
    {
        var src = new Dictionary<string, PinnedPose>
        {
            ["HoleScene"] = new PinnedPose(new Vector3(1.5f, 1.2f, -2.25f), 180f),
        };
        string json = PinnedPoseCodec.Serialize(src);
        Assert.Contains("\n", json);                 // 改行で整形されている
        Assert.Contains("  \"version\": 1", json);   // 2スペースインデント
        Assert.Contains("    \"HoleScene\": {", json); // env エントリは 4スペースインデント
        // 整形しても Parse（\s* 許容）で往復できる。
        var round = PinnedPoseCodec.Parse(json);
        Assert.Single(round);
        Assert.True((round["HoleScene"].Position - new Vector3(1.5f, 1.2f, -2.25f)).magnitude < 1e-4f);
        Assert.Equal(180f, round["HoleScene"].Yaw, 3);
    }

    [Fact]
    public void yaw欠損エントリはskipされ他は残る()
    {
        // Bad は yaw が無い → skip。Good は完全 → 残る。
        string json = "{\"version\":1,\"poses\":{" +
            "\"Bad\":{\"x\":1,\"y\":2,\"z\":3}," +
            "\"Good\":{\"x\":4,\"y\":5,\"z\":6,\"yaw\":90}}}";
        var parsed = PinnedPoseCodec.Parse(json);
        Assert.False(parsed.ContainsKey("Bad"));
        Assert.True(parsed.ContainsKey("Good"));
        Assert.Equal(90f, parsed["Good"].Yaw, 3);
    }
}
