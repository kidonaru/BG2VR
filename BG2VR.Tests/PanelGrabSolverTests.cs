using UnityEngine;
using Xunit;
using BG2VR.WorldUi;
using BG2VR.Locomotion; // GrabMoveSolver.YawRotation（ECall 回避の yaw 構築）

public class PanelGrabSolverTests
{
    private const float Eps = 1e-4f;

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True((expected - actual).sqrMagnitude < Eps * Eps,
            $"expected {expected.x},{expected.y},{expected.z} but {actual.x},{actual.y},{actual.z}");
    }

    // q と −q は同一回転＝プローブベクトルの回転結果で比較（符号非依存・ECall 回避）。
    private static void AssertSameRotation(Quaternion expected, Quaternion actual)
    {
        AssertNear(expected * Vector3.forward, actual * Vector3.forward);
        AssertNear(expected * Vector3.up, actual * Vector3.up);
    }

    private static Quaternion Yaw(float deg) => GrabMoveSolver.YawRotation(deg * Mathf.Deg2Rad);

    // X 軸回転の成分直構築（+ でピッチ下向き）。Quaternion.Euler の ECall 回避。
    private static Quaternion RotX(float deg)
    {
        float h = deg * Mathf.Deg2Rad * 0.5f;
        return new Quaternion(Mathf.Sin(h), 0f, 0f, Mathf.Cos(h));
    }

    [Fact]
    public void ToFrame_FromFrame_往復が恒等()
    {
        Vector3 framePos = new Vector3(1f, 2f, 3f);
        Quaternion frameRot = Yaw(40f) * RotX(10f);
        Vector3 pos = new Vector3(0.5f, 1f, 2f);
        Quaternion rot = Yaw(70f) * RotX(-20f);

        PanelGrabSolver.ToFrame(framePos, frameRot, pos, rot, out Vector3 lp, out Quaternion lr);
        var back = PanelGrabSolver.FromFrame(framePos, frameRot, lp, lr);
        AssertNear(pos, back.Position);
        AssertSameRotation(rot, back.Rotation);
    }

    [Fact]
    public void 剛体追従_回転中心が手になる()
    {
        // 手 (0,1,0)・identity でパネル (0,1,2)・identity を捕捉 → rel=(0,0,2)。
        // 手をその場で +90° yaw → パネルは手を中心に 90° 振られ (2,1,0)・Yaw(90) になる。
        Vector3 hand = new Vector3(0f, 1f, 0f);
        PanelGrabSolver.ToFrame(hand, Quaternion.identity, new Vector3(0f, 1f, 2f), Quaternion.identity,
            out Vector3 rel, out Quaternion relRot);
        AssertNear(new Vector3(0f, 0f, 2f), rel);

        var pose = PanelGrabSolver.FromFrame(hand, Yaw(90f), rel, relRot);
        AssertNear(new Vector3(2f, 1f, 0f), pose.Position);
        AssertSameRotation(Yaw(90f), pose.Rotation);
    }

    [Fact]
    public void 剛体追従_手の平行移動は1対1で伝わる()
    {
        Vector3 hand0 = new Vector3(0f, 1f, 0.3f);
        PanelGrabSolver.ToFrame(hand0, Quaternion.identity, new Vector3(0f, 1.4f, 1.8f), Yaw(10f),
            out Vector3 rel, out Quaternion relRot);

        Vector3 hand1 = hand0 + new Vector3(0.1f, 0.2f, -0.1f); // 回転なしの純移動
        var pose = PanelGrabSolver.FromFrame(hand1, Quaternion.identity, rel, relRot);
        AssertNear(new Vector3(0.1f, 1.6f, 1.7f), pose.Position);
        AssertSameRotation(Yaw(10f), pose.Rotation);
    }

    [Fact]
    public void 剛体追従_手首ピッチで弧を描いて動く()
    {
        // 手 (0,1,0) でパネル正面 2m を捕捉。手首を +90°（下向き）→ パネルは真下の弧へ (0,-1,0)。
        Vector3 hand = new Vector3(0f, 1f, 0f);
        PanelGrabSolver.ToFrame(hand, Quaternion.identity, new Vector3(0f, 1f, 2f), Quaternion.identity,
            out Vector3 rel, out Quaternion relRot);
        var pose = PanelGrabSolver.FromFrame(hand, RotX(90f), rel, relRot);
        AssertNear(new Vector3(0f, -1f, 0f), pose.Position); // +z 2m が -y 2m へ
        AssertSameRotation(RotX(90f), pose.Rotation);        // 全軸追従＝パネルも寝る（タブレット式）
    }

    [Fact]
    public void PushPull_スティックゼロは不変()
    {
        Vector3 rel = new Vector3(0f, 0f, 2f);
        AssertNear(rel, PanelGrabSolver.PushPull(rel, 0f, 0.016f));
    }

    [Fact]
    public void PushPull_スティック上で距離が乗算的に伸びる_遠ざける方向()
    {
        // 方向の仕様固定（spec §4）: stickY>0（上）= 遠ざける。mag=2, stickY=1, dt=1 → 2·e^(1.0·1·1) ≈ 5.43656
        Vector3 r = PanelGrabSolver.PushPull(new Vector3(0f, 0f, 2f), 1f, 1f);
        Assert.Equal(2f * Mathf.Exp(PanelGrabSolver.PushPullRate), r.magnitude, 3);
        AssertNear(Vector3.forward, r.normalized); // 方向は不変
    }

    [Fact]
    public void PushPull_上限8mでclamp()
    {
        Vector3 r = PanelGrabSolver.PushPull(new Vector3(0f, 0f, 7f), 1f, 1f); // 7e^1≈19 → 8
        Assert.Equal(PanelGrabSolver.MaxDistance, r.magnitude, 3);
    }

    [Fact]
    public void PushPull_下限0_5mでclamp()
    {
        Vector3 r = PanelGrabSolver.PushPull(new Vector3(0f, 0f, 1f), -1f, 2f); // e^-2≈0.135 → 0.5
        Assert.Equal(PanelGrabSolver.MinDistance, r.magnitude, 3);
    }

    [Fact]
    public void PushPull_既に下限未満なら引きでそれ以上縮まない()
    {
        // 下限は min(現在値, MinDistance)＝既に近い場合に押しで跳ねず、引きで現状維持。
        Vector3 r = PanelGrabSolver.PushPull(new Vector3(0f, 0f, 0.3f), -1f, 1f);
        Assert.Equal(0.3f, r.magnitude, 3);
    }

    [Fact]
    public void 長時間合成でも回転ノルムがドリフトしない()
    {
        Quaternion rot = Quaternion.identity;
        Vector3 pos = new Vector3(0f, 0f, 2f);
        Quaternion step = Yaw(1.7f) * RotX(0.9f);
        for (int i = 0; i < 2000; i++)
        {
            var p = PanelGrabSolver.FromFrame(Vector3.zero, step, pos, rot);
            pos = p.Position; rot = p.Rotation;
        }
        float norm = Mathf.Sqrt(rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w);
        Assert.Equal(1f, norm, 3);
    }

    [Fact]
    public void 既定アンカーのdecodeが旧Solveと同値_正面()
    {
        // 旧 PlacementSolver.Solve(zero, forward, 1.5, -0.1, _) の位置 (0,-0.1,1.5) と一致（挙動互換の回帰ガード）。
        var pose = PanelGrabSolver.FromFrame(Vector3.zero, Quaternion.identity,
            new Vector3(0f, -0.1f, 1.5f), Quaternion.identity);
        AssertNear(new Vector3(0f, -0.1f, 1.5f), pose.Position);
        AssertSameRotation(Quaternion.identity, pose.Rotation);
    }

    [Fact]
    public void 既定アンカーのdecodeが旧Solveと同値_yawフレームとorigin()
    {
        // 旧 Solve(origin=(0.2,1.5,0), forward=+x, dist=2) の位置 (2.2,1.5,0) と一致。
        var pose = PanelGrabSolver.FromFrame(new Vector3(0.2f, 1.5f, 0f), Yaw(90f),
            new Vector3(0f, 0f, 2f), Quaternion.identity);
        AssertNear(new Vector3(2.2f, 1.5f, 0f), pose.Position);
        AssertSameRotation(Yaw(90f), pose.Rotation); // パネル +z が +x（= 旧 LookRotation(forward) と同じ）
    }
}
