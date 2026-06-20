using BG2VR.Talk2DBackdrop;
using UnityEngine;
using Xunit;

public class BackdropSolverTests
{
    // Afternoon 実測相当の値（spec §1）: cam z=1.96 / bg z=-6.27（距離 8.23m）
    private static readonly Vector3 Cam = new Vector3(0f, 0f, 1.96f);
    private static readonly Vector3 OrigPos = new Vector3(0f, 0f, -6.27f);
    private static readonly Vector3 OrigScale = new Vector3(1.01f, 1f, 0.56f);

    [Fact]
    public void Push_カメラ基準の半直線上で距離とスケールが倍率倍になる()
    {
        var r = BackdropSolver.Push(OrigPos, OrigScale, Cam, 4f, 1000f);

        Assert.Equal(4f, r.EffectiveMul, 3);
        // 方向不変（x/y は 0 のまま）・カメラからの距離が 4 倍
        Assert.Equal(0f, r.LocalPosition.x, 4);
        Assert.Equal(0f, r.LocalPosition.y, 4);
        Assert.Equal(1.96f - 8.23f * 4f, r.LocalPosition.z, 3);
        // スケールも同倍率（角度サイズ保存）
        Assert.Equal(OrigScale.x * 4f, r.LocalScale.x, 4);
        Assert.Equal(OrigScale.y * 4f, r.LocalScale.y, 4);
        Assert.Equal(OrigScale.z * 4f, r.LocalScale.z, 4);
    }

    [Fact]
    public void Push_倍率1は元値をそのまま返す()
    {
        var r = BackdropSolver.Push(OrigPos, OrigScale, Cam, 1f, 1000f);

        Assert.Equal(OrigPos.z, r.LocalPosition.z, 4);
        Assert.Equal(OrigScale.x, r.LocalScale.x, 4);
        Assert.Equal(1f, r.EffectiveMul, 4);
    }

    [Fact]
    public void Push_同じ元値からの再適用は累積しない()
    {
        var a = BackdropSolver.Push(OrigPos, OrigScale, Cam, 4f, 85.47f);
        var b = BackdropSolver.Push(OrigPos, OrigScale, Cam, 4f, 85.47f);

        Assert.Equal(a.LocalPosition.z, b.LocalPosition.z, 5);
        Assert.Equal(a.LocalScale.x, b.LocalScale.x, 5);
    }

    [Fact]
    public void ComputeEffectiveMul_far制約でclampされる()
    {
        // far=50 → maxDist=42.5 → kMax=42.5/8.23=5.164…（mul=8 を希望しても 5.16 に抑える）
        // 期待値は実装式の再掲でなく独立に手計算した定数（トートロジー回避・plan-review 指摘）。
        float k = BackdropSolver.ComputeEffectiveMul(8f, 8.23f, 50f);

        Assert.Equal(5.164f, k, 3);
    }

    [Fact]
    public void ComputeEffectiveMul_実測far85では倍率8がそのまま通る()
    {
        // far=85.47 → kMax=8.83 > 8（スライダー上限 8 が実機 far に収まる確認）
        float k = BackdropSolver.ComputeEffectiveMul(8f, 8.23f, 85.47f);

        Assert.Equal(8f, k, 3);
    }

    [Fact]
    public void ComputeEffectiveMul_下限1を割らない()
    {
        // far が極端に小さく kMax<1 でも 1（=元より近づける退行をしない）
        float k = BackdropSolver.ComputeEffectiveMul(4f, 8.23f, 5f);

        Assert.Equal(1f, k, 4);
    }

    [Fact]
    public void ComputeEffectiveMul_距離ゼロ退避()
    {
        // カメラと同位置＝押す方向が定まらない → 1（no-op）
        float k = BackdropSolver.ComputeEffectiveMul(4f, 0f, 85.47f);

        Assert.Equal(1f, k, 4);
    }
}
