using BG2VR.AhhnVr;
using UnityEngine;
using Xunit;

public class AhhnEatJudgeTests
{
    [Fact]
    public void Hit_しきい値内_True()
    {
        Assert.True(AhhnEatJudge.Hit(Vector3.zero, new Vector3(0.05f, 0f, 0f), 0.1f));
    }

    [Fact]
    public void Hit_しきい値ちょうど_True()
    {
        // 距離 == threshold は成功扱い（<=）。
        Assert.True(AhhnEatJudge.Hit(Vector3.zero, new Vector3(0.1f, 0f, 0f), 0.1f));
    }

    [Fact]
    public void Hit_しきい値外_False()
    {
        Assert.False(AhhnEatJudge.Hit(Vector3.zero, new Vector3(0.2f, 0f, 0f), 0.1f));
    }

    [Fact]
    public void Hit_3D斜め距離で評価する()
    {
        // (0.06,0.06,0.06) の距離 ≈ 0.1039 > 0.1 → 外。各軸単独では 0.06<0.1 でも 3D 距離で落ちる。
        Assert.False(AhhnEatJudge.Hit(Vector3.zero, new Vector3(0.06f, 0.06f, 0.06f), 0.1f));
        // (0.05,0.05,0.05) の距離 ≈ 0.0866 < 0.1 → 内。
        Assert.True(AhhnEatJudge.Hit(Vector3.zero, new Vector3(0.05f, 0.05f, 0.05f), 0.1f));
    }

    [Fact]
    public void RisingEdge_falseからtrueで1回true_prevも更新()
    {
        bool prev = false;
        Assert.True(AhhnEatJudge.RisingEdge(ref prev, true));
        Assert.True(prev);
    }

    [Fact]
    public void RisingEdge_押しっぱなしは2度目以降false()
    {
        bool prev = false;
        AhhnEatJudge.RisingEdge(ref prev, true); // 1 回目 = true
        Assert.False(AhhnEatJudge.RisingEdge(ref prev, true)); // 押し続け = false
    }

    [Fact]
    public void RisingEdge_離して再押下で再発火()
    {
        bool prev = true;
        Assert.False(AhhnEatJudge.RisingEdge(ref prev, false)); // 離す
        Assert.True(AhhnEatJudge.RisingEdge(ref prev, true));   // 再押下 = true
    }
}
