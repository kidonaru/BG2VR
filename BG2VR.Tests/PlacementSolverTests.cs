using BG2VR.WorldUi;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests;

public class PlacementSolverTests
{
    private const float T0 = 20f; // 直立帯しきい角（度）

    // 旧 Solve（位置合成）のテストは PanelGrabSolverTests の
    // 「既定アンカーのdecodeが旧Solveと同値_*」2 件へ移管済み（アンカー化リファクタ・plan Task 3）。
    // 旧 YawForward は頭追従廃止（rig 軸相対化・spec §5）で呼出元が消えたため削除。
    // 旧 PanelScale は VrUiPanel の実寸メッシュ化（quad localScale 廃止）で prod 未使用となったため
    // API ごと削除（2026-06-06 grep 確認）。

    private static Vector3 Fwd(Quaternion q) => q * new Vector3(0f, 0f, 1f);

    [Fact]
    public void 正面は無回転()
    {
        var f = Fwd(PlacementSolver.ComputeRotation(new Vector3(0f, 0f, 2f), T0));
        Assert.True((f - new Vector3(0f, 0f, 1f)).magnitude < 1e-4f);
    }

    [Fact]
    public void 直立帯内は完全直立()
    {
        // 仰角 atan2(0.5, 2) ≈ 14.0° < 20° → +Z は水平のまま（パネル直立）
        var f = Fwd(PlacementSolver.ComputeRotation(new Vector3(0f, 0.5f, 2f), T0));
        Assert.Equal(0f, f.y, 4);
        Assert.True(f.z > 0.999f);
    }

    [Fact]
    public void 超過分だけ傾く_上では読み面が俯く()
    {
        // 仰角 45°・しきい 20° → +Z の仰角 25°（上向き）＝読み面 −Z は視点側へ俯く
        var f = Fwd(PlacementSolver.ComputeRotation(new Vector3(0f, 2f, 2f), T0));
        float expected = (45f - T0) * Mathf.Deg2Rad;
        Assert.Equal(Mathf.Sin(expected), f.y, 3);
        Assert.Equal(Mathf.Cos(expected), f.z, 3);
        Assert.Equal(0f, f.x, 4);
    }

    [Fact]
    public void 下側も対称()
    {
        var up = Fwd(PlacementSolver.ComputeRotation(new Vector3(0f, 2f, 2f), T0));
        var down = Fwd(PlacementSolver.ComputeRotation(new Vector3(0f, -2f, 2f), T0));
        Assert.Equal(up.y, -down.y, 4);
        Assert.Equal(up.z, down.z, 4);
    }

    [Fact]
    public void ヨーは方位に一致しロールは0()
    {
        // 右 45°・直立帯内（高さ 0）
        var q = PlacementSolver.ComputeRotation(new Vector3(2f, 0f, 2f), T0);
        var f = Fwd(q);
        Assert.Equal(Mathf.Sin(45f * Mathf.Deg2Rad), f.x, 3);
        Assert.Equal(0f, f.y, 4);
        // ロール 0: panel-local +X（right）が水平のまま
        var r = q * new Vector3(1f, 0f, 0f);
        Assert.Equal(0f, r.y, 4);
    }

    [Fact]
    public void しきい境界で連続()
    {
        var below = Fwd(PlacementSolver.ComputeRotation(
            new Vector3(0f, Mathf.Tan(19.9f * Mathf.Deg2Rad) * 2f, 2f), T0));
        var above = Fwd(PlacementSolver.ComputeRotation(
            new Vector3(0f, Mathf.Tan(20.1f * Mathf.Deg2Rad) * 2f, 2f), T0));
        Assert.True((below - above).magnitude < 0.01f);
    }

    [Fact]
    public void 原点縮退は無回転()
    {
        var q = PlacementSolver.ComputeRotation(Vector3.zero, T0);
        Assert.Equal(0f, q.x, 5); Assert.Equal(0f, q.y, 5);
        Assert.Equal(0f, q.z, 5); Assert.Equal(1f, q.w, 5);
    }

    [Fact]
    public void 回転は単位quaternion()
    {
        var q = PlacementSolver.ComputeRotation(new Vector3(1.2f, -0.8f, 1.7f), T0);
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        Assert.Equal(1f, m, 4);
    }

    [Theory]
    [InlineData(0f, 0f, 2f)]
    [InlineData(1.5f, 0.4f, 1.5f)]
    [InlineData(-2f, -1.2f, 0.7f)]
    [InlineData(0.3f, 1.8f, -2.5f)]
    public void EncodeDecode往復一致(float x, float y, float z)
    {
        var v = new Vector3(x, y, z);
        var p = PlacementSolver.Encode(v);
        var back = PlacementSolver.Decode(p.HorizDist, p.Height, p.YawDeg);
        Assert.True((back - v).magnitude < 1e-4f);
    }

    [Fact]
    public void ヨー符号は右が正()
    {
        Assert.True(PlacementSolver.Encode(new Vector3(1f, 0f, 1f)).YawDeg > 0f);
        Assert.True(PlacementSolver.Encode(new Vector3(-1f, 0f, 1f)).YawDeg < 0f);
    }
}
