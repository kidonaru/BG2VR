using UnityEngine;
using Xunit;
using BG2VR.Locomotion;

public class GrabMoveSolverTests
{
    private const float Eps = 1e-4f;

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True((expected - actual).sqrMagnitude < Eps * Eps,
            $"expected {expected.x},{expected.y},{expected.z} but {actual.x},{actual.y},{actual.z}");
    }

    // Quaternion.AngleAxis(deg, up) の ECall 回避（テストでも solver の直構築を使う）。
    private static Quaternion Yaw(float deg) => GrabMoveSolver.YawRotation(deg * Mathf.Deg2Rad);

    // X 軸回転の成分直構築（+ でピッチ下向き）。Quaternion.Euler の ECall 回避。
    private static Quaternion RotX(float deg)
    {
        float h = deg * Mathf.Deg2Rad * 0.5f;
        return new Quaternion(Mathf.Sin(h), 0f, 0f, Mathf.Cos(h));
    }

    [Fact]
    public void 平行移動_手の移動の逆方向へrigが動く()
    {
        // 手を tracking 空間で +z に 0.1 動かす → rig は -z に 0.1（空間を押した＝自分が下がる）。
        // 手を引けば（-z）rig は +z（前進）= 「掴んで引っ張る」。
        var p = GrabMoveSolver.Step(
            Vector3.zero, Quaternion.identity, 1f,
            new Vector3(0f, 1f, 0.5f), Quaternion.identity,
            new Vector3(0f, 1f, 0.6f), Quaternion.identity);
        AssertNear(new Vector3(0f, 0f, -0.1f), p.Position);
        Assert.True(Mathf.Abs(p.Rotation.y) < Eps);   // 回転なし
    }

    [Fact]
    public void 回転_手のworld位置が固定され回転中心になる()
    {
        Vector3 handLocal = new Vector3(0.2f, 1f, 0.4f);
        Vector3 rigPos = new Vector3(5f, 0f, 2f);
        Quaternion rigRot = Yaw(45f);
        const float s = 2f;

        Vector3 handWorldBefore = rigPos + rigRot * (handLocal * s);
        var p = GrabMoveSolver.Step(rigPos, rigRot, s,
            handLocal, Yaw(0f), handLocal, Yaw(30f));   // 手を +30° ひねる（位置は同じ）
        Vector3 handWorldAfter = p.Position + p.Rotation * (handLocal * s);

        AssertNear(handWorldBefore, handWorldAfter);     // 回転中心 = 手（grip 移動の核心）
        Assert.True(GrabMoveSolver.TryYawOf(p.Rotation, out float yawRad));
        Assert.Equal(15f, yawRad * Mathf.Rad2Deg, 2);    // 掴んだ空間の固定＝rig は 45−30=15°
    }

    [Fact]
    public void WorldScale_移動量がrigScale倍される()
    {
        // rigScale=2（WorldScale 0.5）: 手の 0.1m は world では 0.2。
        var p = GrabMoveSolver.Step(
            Vector3.zero, Quaternion.identity, 2f,
            Vector3.zero, Quaternion.identity,
            new Vector3(0.1f, 0f, 0f), Quaternion.identity);
        AssertNear(new Vector3(-0.2f, 0f, 0f), p.Position);
    }

    [Fact]
    public void 特異点_手が真上向きならyawをスキップし移動だけ通す()
    {
        // X 軸 −90° 回転（forward が +y を向く）= 水平成分ゼロの特異点。
        var up = new Quaternion(-0.70710678f, 0f, 0f, 0.70710678f);
        Assert.False(GrabMoveSolver.TryYawOf(up, out _));

        var p = GrabMoveSolver.Step(
            Vector3.zero, Quaternion.identity, 1f,
            Vector3.zero, up,
            new Vector3(0f, 0f, 0.1f), up);
        AssertNear(new Vector3(0f, 0f, -0.1f), p.Position);  // 移動は通る
        Assert.True(Mathf.Abs(p.Rotation.y) < Eps);          // 回転は 0
    }

    [Fact]
    public void Wrap_180度をまたぐyaw差分が短い方の弧になる()
    {
        // 170° → −170°（実際の回転は +20°）。wrap が無いと −340° と誤計算される。
        Vector3 handLocal = new Vector3(0f, 1f, 0.3f);
        var p = GrabMoveSolver.Step(
            Vector3.zero, Quaternion.identity, 1f,
            handLocal, Yaw(170f), handLocal, Yaw(-170f));
        Assert.True(GrabMoveSolver.TryYawOf(p.Rotation, out float yawRad));
        Assert.Equal(-20f, yawRad * Mathf.Rad2Deg, 2);       // rig は −20°（= −deltaYaw）
    }

    [Fact]
    public void 累積と逆適用_移動回転の往復でrigが元のposeへ戻る()
    {
        Vector3 accumPos = Vector3.zero;
        Quaternion accumRot = new Quaternion(0f, 0f, 0f, 1f);
        Vector3 rigPos = new Vector3(1f, 2f, 3f);
        Quaternion rigRot = Yaw(30f);
        Vector3 startPos = rigPos;

        Vector3 prevP = new Vector3(0f, 1f, 0.5f);
        Quaternion prevR = Yaw(0f);
        var hands = new (Vector3 p, Quaternion r)[]
        {
            (new Vector3(0.1f, 1.1f, 0.4f), Yaw(10f)),
            (new Vector3(0.2f, 0.9f, 0.3f), Yaw(-20f)),
            (new Vector3(0.0f, 1.0f, 0.6f), Yaw(5f)),
        };
        foreach (var h in hands)
        {
            var np = GrabMoveSolver.Step(rigPos, rigRot, 1f, prevP, prevR, h.p, h.r);
            GrabMoveSolver.AccumulateDelta(rigPos, rigRot, np.Position, np.Rotation, ref accumPos, ref accumRot);
            rigPos = np.Position; rigRot = np.Rotation;
            prevP = h.p; prevR = h.r;
        }

        var restored = GrabMoveSolver.ApplyInverse(accumPos, accumRot, rigPos, rigRot);
        AssertNear(startPos, restored.Position);
        Assert.True(GrabMoveSolver.TryYawOf(restored.Rotation, out float yawRad));
        Assert.Equal(30f, yawRad * Mathf.Rad2Deg, 2);
    }

    [Fact]
    public void Twist_純粋なworldY回転は1対1()
    {
        // delta = Yaw(30) → twist はちょうど 30°（従来の forward 投影と同値になる基本ケース）。
        float tw = GrabMoveSolver.TwistYawOf(Yaw(30f));
        Assert.Equal(30f, tw * Mathf.Rad2Deg, 3);
    }

    [Fact]
    public void Twist_ピッチ構えでのworldY回転も1対1()
    {
        // 下向き 60° に構えたまま world Y で 30° 回す: delta = curr*conj(prev) は純粋な Yaw(30)。
        Quaternion prev = RotX(60f);
        Quaternion curr = Yaw(30f) * prev;
        var p = GrabMoveSolver.Step(
            Vector3.zero, Quaternion.identity, 1f,
            new Vector3(0f, 1f, 0.4f), prev, new Vector3(0f, 1f, 0.4f), curr);
        Assert.True(GrabMoveSolver.TryYawOf(p.Rotation, out float yawRad));
        Assert.Equal(-30f, yawRad * Mathf.Rad2Deg, 2);   // rig は −deltaYaw（増幅なし）
    }

    [Fact]
    public void Twist_ローカルup軸回転はピッチ分減衰し増幅されない()
    {
        // 下向き 60° 構えで「コントローラのローカル up 軸」まわりに 10° 回す。
        // 旧 forward 投影は 1/cos(60°)=2 倍の 20° に増幅していた（「回転しすぎ」の原因）。
        // swing-twist は軸の Y 成分分だけ＝2·atan2(cos60·sin5°, cos5°) ≈ 5.01°（euler-y 抽出と同傾向）。
        Quaternion prev = RotX(60f);
        Quaternion curr = prev * Yaw(10f);   // 右掛け = ローカル軸回転
        float tw = GrabMoveSolver.TwistYawOf(curr * new Quaternion(-prev.x, -prev.y, -prev.z, prev.w));
        float expected = 2f * Mathf.Atan2(
            Mathf.Cos(60f * Mathf.Deg2Rad) * Mathf.Sin(5f * Mathf.Deg2Rad),
            Mathf.Cos(5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
        Assert.Equal(expected, tw * Mathf.Rad2Deg, 2);
        Assert.True(tw * Mathf.Rad2Deg < 10f);           // 入力角を超えない（増幅なしの回帰ガード）
    }

    [Fact]
    public void Twist_真下向きの垂直軸ひねりもyawとして拾える()
    {
        // 真下向き（forward 垂直）で world Y 軸 20° ひねり。旧 forward 投影は特異点で 0 だった。
        Quaternion prev = RotX(90f);
        Quaternion curr = Yaw(20f) * prev;
        var p = GrabMoveSolver.Step(
            Vector3.zero, Quaternion.identity, 1f,
            new Vector3(0f, 1f, 0.3f), prev, new Vector3(0f, 1f, 0.3f), curr);
        Assert.True(GrabMoveSolver.TryYawOf(p.Rotation, out float yawRad));
        Assert.Equal(-20f, yawRad * Mathf.Rad2Deg, 2);
    }

    [Fact]
    public void Twist_符号反転したquaternionでも同じ値()
    {
        // OpenVR は q と −q（同一回転）を返し得る。twist は符号反転に不変であること。
        Quaternion d = Yaw(15f);
        Quaternion neg = new Quaternion(-d.x, -d.y, -d.z, -d.w);
        Assert.Equal(
            GrabMoveSolver.TwistYawOf(d) * Mathf.Rad2Deg,
            GrabMoveSolver.TwistYawOf(neg) * Mathf.Rad2Deg, 3);
    }

    [Fact]
    public void TryYawOfForward_水平forwardからyawを抽出し再構築と往復一致()
    {
        Assert.True(GrabMoveSolver.TryYawOfForward(new Vector3(1f, 0f, 0f), out float yaw));
        Assert.Equal(90f, yaw * Mathf.Rad2Deg, 3);
        // YawRotation で再構築した quaternion からも同じ yaw が取れる（runner の平滑済み forward 再構築経路）
        Assert.True(GrabMoveSolver.TryYawOf(GrabMoveSolver.YawRotation(yaw), out float yaw2));
        Assert.Equal(90f, yaw2 * Mathf.Rad2Deg, 3);
    }

    [Fact]
    public void TryYawOfForward_垂直forwardは特異点()
    {
        Assert.False(GrabMoveSolver.TryYawOfForward(new Vector3(0f, 1f, 0f), out _));
        Assert.False(GrabMoveSolver.TryYawOfForward(new Vector3(0f, -1f, 0.005f), out _)); // ほぼ垂直も弾く
    }

    [Fact]
    public void リセットは外部の垂直オフセットを保存する()
    {
        // grip 移動 → fork の目線高さ変更（外部の垂直移動）→ さらに移動 → リセット。
        // リセットは grip 分のみ巻き戻し、垂直オフセットは残る（可換性・spec §5）。
        Vector3 accumPos = Vector3.zero;
        Quaternion accumRot = new Quaternion(0f, 0f, 0f, 1f);
        Vector3 rigPos = Vector3.zero;
        Quaternion rigRot = Quaternion.identity;

        var np1 = GrabMoveSolver.Step(rigPos, rigRot, 1f,
            new Vector3(0f, 1f, 0.5f), Yaw(0f), new Vector3(0.2f, 1f, 0.3f), Yaw(25f));
        GrabMoveSolver.AccumulateDelta(rigPos, rigRot, np1.Position, np1.Rotation, ref accumPos, ref accumRot);
        rigPos = np1.Position; rigRot = np1.Rotation;

        rigPos += new Vector3(0f, 0.25f, 0f);  // fork UpdateVerticalOffset 相当（grip の累積外）

        var np2 = GrabMoveSolver.Step(rigPos, rigRot, 1f,
            new Vector3(0.2f, 1f, 0.3f), Yaw(25f), new Vector3(0.1f, 1f, 0.5f), Yaw(-10f));
        GrabMoveSolver.AccumulateDelta(rigPos, rigRot, np2.Position, np2.Rotation, ref accumPos, ref accumRot);
        rigPos = np2.Position; rigRot = np2.Rotation;

        var restored = GrabMoveSolver.ApplyInverse(accumPos, accumRot, rigPos, rigRot);
        AssertNear(new Vector3(0f, 0.25f, 0f), restored.Position);  // 垂直オフセットだけ残る
        Assert.True(Mathf.Abs(restored.Rotation.y) < 1e-3f);        // yaw は 0 に戻る
    }
}
