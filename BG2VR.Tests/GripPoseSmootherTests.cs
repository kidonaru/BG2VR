using UnityEngine;
using Xunit;
using BG2VR.Locomotion;

public class GripPoseSmootherTests
{
    private const float Dt = 1f / 60f;

    // quaternion の成分内積（|dot|→1 で同一回転に近い）。ECall 回避のため手計算。
    private static float QDot(Quaternion a, Quaternion b)
        => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

    private static Quaternion Yaw(float deg) => GrabMoveSolver.YawRotation(deg * Mathf.Deg2Rad);

    [Fact]
    public void Tau0はパススルー()
    {
        var s = new GripPoseSmoother();
        var raw = new Vector3(1f, 2f, 3f);
        s.Update(raw, Yaw(40f), Dt, 0f, out Vector3 pos, out Quaternion rot);
        Assert.Equal(raw, pos);
        Assert.True(Mathf.Abs(QDot(rot, Yaw(40f))) > 0.99999f);
    }

    [Fact]
    public void 初回はrawをそのまま採用()
    {
        var s = new GripPoseSmoother();
        s.Update(new Vector3(5f, 0f, 0f), Yaw(90f), Dt, 0.2f, out Vector3 pos, out Quaternion rot);
        Assert.Equal(new Vector3(5f, 0f, 0f), pos);
        Assert.True(Mathf.Abs(QDot(rot, Yaw(90f))) > 0.99999f);
    }

    [Fact]
    public void ステップ入力に収束する()
    {
        var s = new GripPoseSmoother();
        s.Update(Vector3.zero, Yaw(0f), Dt, 0.1f, out _, out _);   // シード
        Vector3 pos = default; Quaternion rot = default;
        for (int i = 0; i < 120; i++)                              // 2 秒（τ=0.1 の 20 倍）
            s.Update(new Vector3(1f, 0f, 0f), Yaw(30f), Dt, 0.1f, out pos, out rot);
        Assert.True((pos - new Vector3(1f, 0f, 0f)).sqrMagnitude < 1e-6f);
        Assert.True(Mathf.Abs(QDot(rot, Yaw(30f))) > 0.9999f);
    }

    [Fact]
    public void 半球補正_符号反転rawへの収束が遠回りしない()
    {
        // OpenVR は同一回転を q/−q どちらでも返し得る。seed=Yaw(30°) に対し
        // 「別角度の対蹠点表現」raw=−Yaw(40°) を流し続けると、半球補正が無い場合は
        // 4D 成分 lerp が原点近傍を横切り、正規化後の回転が 30→40° の帯域を大きく外れて
        // 暴れる（≈210° 経由の大回り）。補正があれば全フレーム 30°→40° へ直行する。
        var s = new GripPoseSmoother();
        s.Update(Vector3.zero, Yaw(30f), Dt, 0.2f, out _, out _);
        var target = Yaw(40f);
        var rawNeg = new Quaternion(-target.x, -target.y, -target.z, -target.w);
        Quaternion rot = default;
        for (int i = 0; i < 120; i++)
        {
            s.Update(Vector3.zero, rawNeg, Dt, 0.2f, out _, out rot);
            Assert.True(GrabMoveSolver.TryYawOf(rot, out float yawRad));
            float yawDeg = yawRad * Mathf.Rad2Deg;
            Assert.True(yawDeg > 29f && yawDeg < 41f, $"frame {i}: yaw={yawDeg}（帯域逸脱＝大回り）");
        }
        Assert.True(Mathf.Abs(QDot(rot, target)) > 0.9999f);   // 最終的に 40° へ収束
    }

    [Fact]
    public void Resetで次のUpdateが再シードになる()
    {
        var s = new GripPoseSmoother();
        s.Update(Vector3.zero, Yaw(0f), Dt, 0.2f, out _, out _);
        s.Reset();
        s.Update(new Vector3(9f, 9f, 9f), Yaw(120f), Dt, 0.2f, out Vector3 pos, out Quaternion rot);
        Assert.Equal(new Vector3(9f, 9f, 9f), pos);                // 平滑遅れを持ち越さない
        Assert.True(Mathf.Abs(QDot(rot, Yaw(120f))) > 0.99999f);
    }
}
