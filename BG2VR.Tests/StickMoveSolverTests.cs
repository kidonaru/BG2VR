using UnityEngine;
using Xunit;
using BG2VR.Locomotion;

public class StickMoveSolverTests
{
    [Fact]
    public void BelowDeadzone_ReturnsZero()
    {
        var d = StickMoveSolver.ComputeDelta(new Vector2(0.1f, 0f), Vector3.forward, Vector3.right, 1.5f, 0.15f, 0.1f);
        Assert.Equal(Vector3.zero, d);
    }

    [Fact]
    public void ForwardStick_MovesAlongForward()
    {
        // 速度2 × dt0.5 = 0.5 を forward(+Z) 方向へ
        var d = StickMoveSolver.ComputeDelta(new Vector2(0f, 1f), Vector3.forward, Vector3.right, 2f, 0.15f, 0.5f);
        Assert.Equal(0f, d.x, 4);
        Assert.Equal(0f, d.y, 4);
        Assert.Equal(1f, d.z, 4);
    }

    [Fact]
    public void RightStick_StrafesAlongRight()
    {
        var d = StickMoveSolver.ComputeDelta(new Vector2(1f, 0f), Vector3.forward, Vector3.right, 2f, 0.15f, 0.5f);
        Assert.Equal(1f, d.x, 4);
        Assert.Equal(0f, d.y, 4);
        Assert.Equal(0f, d.z, 4);
    }

    [Fact]
    public void Speed_ScalesMagnitude()
    {
        var slow = StickMoveSolver.ComputeDelta(new Vector2(0f, 1f), Vector3.forward, Vector3.right, 1f, 0.15f, 1f);
        var fast = StickMoveSolver.ComputeDelta(new Vector2(0f, 1f), Vector3.forward, Vector3.right, 3f, 0.15f, 1f);
        Assert.True(fast.magnitude > slow.magnitude);
    }

    [Fact]
    public void NoVerticalComponent()
    {
        // headForward/Right が水平でも上下は出ない
        var d = StickMoveSolver.ComputeDelta(new Vector2(0.7f, 0.7f), Vector3.forward, Vector3.right, 2f, 0.15f, 0.5f);
        Assert.Equal(0f, d.y, 4);
    }
}
