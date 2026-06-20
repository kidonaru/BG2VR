using UnityEngine;
using Xunit;
using BG2VR.CameraFollow;

public class CameraFollowStateTests
{
    [Fact]
    public void 初回Stepは差分ゼロでbaselineを設定する()
    {
        var s = new CameraFollowState();
        Assert.Equal(Vector3.zero, s.Step(new Vector3(1f, 2f, 3f)));
    }

    [Fact]
    public void 連続Stepは前回位置との差分を返す()
    {
        var s = new CameraFollowState();
        s.Step(new Vector3(1f, 2f, 3f));
        Assert.Equal(new Vector3(0.5f, 0f, 1f), s.Step(new Vector3(1.5f, 2f, 4f)));
    }

    [Fact]
    public void 静止中は差分ゼロ()
    {
        var s = new CameraFollowState();
        s.Step(new Vector3(1f, 2f, 3f));
        Assert.Equal(Vector3.zero, s.Step(new Vector3(1f, 2f, 3f)));
    }

    [Fact]
    public void Invalidate後の初回Stepは差分ゼロ_大ジャンプを適用しない()
    {
        var s = new CameraFollowState();
        s.Step(new Vector3(1f, 2f, 3f));
        s.Invalidate();
        // OFF 中/遷移中に溜まった移動分を一括ジャンプさせない（spec §3.2）
        Assert.Equal(Vector3.zero, s.Step(new Vector3(10f, 20f, 30f)));
    }

    [Fact]
    public void Invalidate後も2回目以降のStepは差分を返す()
    {
        var s = new CameraFollowState();
        s.Step(new Vector3(1f, 2f, 3f));
        s.Invalidate();
        s.Step(new Vector3(10f, 20f, 30f));
        Assert.Equal(new Vector3(1f, 0f, 0f), s.Step(new Vector3(11f, 20f, 30f)));
    }

    [Fact]
    public void Invalidate連続2回でも初回Stepは差分ゼロ_冪等()
    {
        var s = new CameraFollowState();
        s.Step(new Vector3(1f, 2f, 3f));
        s.Invalidate();
        s.Invalidate();
        Assert.Equal(Vector3.zero, s.Step(new Vector3(10f, 20f, 30f)));
    }
}
