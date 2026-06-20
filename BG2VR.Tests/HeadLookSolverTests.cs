using BG2VR.HeadLook;
using UnityEngine;
using Xunit;

public class HeadLookSolverTests
{
    const float Eps = 0.01f;

    // ---- ComputeOffsetAngles ----

    [Fact]
    public void Offset_正面のターゲットはゼロ角()
    {
        var a = HeadLookSolver.ComputeOffsetAngles(
            Vector3.zero, new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 0, 0),
            new Vector3(0, 0, 5));
        Assert.True(Mathf.Abs(a.Yaw) < Eps && Mathf.Abs(a.Pitch) < Eps);
    }

    [Fact]
    public void Offset_右45度のターゲットはYaw45()
    {
        var a = HeadLookSolver.ComputeOffsetAngles(
            Vector3.zero, new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 0, 0),
            new Vector3(1, 0, 1));
        Assert.True(Mathf.Abs(a.Yaw - 45f) < Eps);
        Assert.True(Mathf.Abs(a.Pitch) < Eps);
    }

    [Fact]
    public void Offset_上45度のターゲットはPitch45()
    {
        var a = HeadLookSolver.ComputeOffsetAngles(
            Vector3.zero, new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 0, 0),
            new Vector3(0, 1, 1));
        Assert.True(Mathf.Abs(a.Pitch - 45f) < Eps);
        Assert.True(Mathf.Abs(a.Yaw) < Eps);
    }

    [Fact]
    public void Offset_背後のターゲットはYaw180側()
    {
        var a = HeadLookSolver.ComputeOffsetAngles(
            Vector3.zero, new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 0, 0),
            new Vector3(0.01f, 0, -5));
        Assert.True(Mathf.Abs(a.Yaw) > 170f);
    }

    [Fact]
    public void Offset_ゼロ距離はゼロ角()
    {
        var a = HeadLookSolver.ComputeOffsetAngles(
            Vector3.one, new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 0, 0),
            Vector3.one);
        Assert.True(Mathf.Abs(a.Yaw) < Eps && Mathf.Abs(a.Pitch) < Eps);
    }

    [Fact]
    public void Offset_軸が斜めでも軸基準で角度が出る()
    {
        // fwd=+X, right=-Z, up=+Y の頭（90°回転した頭）。ターゲットは world +X = 頭正面
        var a = HeadLookSolver.ComputeOffsetAngles(
            Vector3.zero, new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1),
            new Vector3(5, 0, 0));
        Assert.True(Mathf.Abs(a.Yaw) < Eps && Mathf.Abs(a.Pitch) < Eps);
    }

    // ---- Step（ヒステリシス/デッドゾーン/平滑/目） ----

    static HeadLookSolver.Angles Ang(float yaw, float pitch)
        => new HeadLookSolver.Angles { Yaw = yaw, Pitch = pitch };

    /// <summary>
    /// テスト用 Tuning fixture。Configs.yaml の既定値と同じ値でテストする
    /// （既定値を変更したらここも手で揃える。境界に依存するテストは値を追従修正すること）。
    /// </summary>
    static readonly HeadLookSolver.Tuning T = new HeadLookSolver.Tuning
    {
        EngageYawDeg = 45f,
        EngagePitchDeg = 45f,
        ReleaseYawMarginDeg = 20f,  // → release yaw = 65°
        ReleasePitchMarginDeg = 20f, // → release pitch = 65°
        DeadZoneStartDeg = 10f,
        DeadZoneStopDeg = 1f,
        HeadTau = 0.30f,
        EyeTau = 0.025f, // ほぼ「1フレで残り半分」@60fps
        HeadRatio = 0.45f,
        EyeYawRatio = 0.2f,
        EyePitchRatio = 0.1f,
    };

    /// <summary>定常入力で n 秒ぶん回す（60fps・fixture Tuning）。</summary>
    static HeadLookSolver.StepResult Run(LookAtState s, float yaw, float pitch, float seconds)
    {
        var r = default(HeadLookSolver.StepResult);
        int n = (int)(seconds * 60f);
        for (int i = 0; i < n; i++)
            r = HeadLookSolver.Step(s, Ang(yaw, pitch), 1f / 60f, in T);
        return r;
    }

    [Fact]
    public void Step_範囲内でengageし首は計算角のHeadRatio倍へ収束()
    {
        var s = new LookAtState();
        var r = Run(s, 30f, 0f, seconds: 3f);
        Assert.True(s.Engaged);
        // デッドゾーン Stop=1° の残差で 29〜30° で凍結するため許容は 1° 幅（plan-review 🟡#3）
        Assert.True(Mathf.Abs(r.HeadYawApplied - 30f * T.HeadRatio) < 1.0f);
    }

    [Fact]
    public void Step_engage範囲外では追従しない()
    {
        var s = new LookAtState();
        var r = Run(s, 50f, 0f, seconds: 3f); // 50° > engage 45°
        Assert.False(s.Engaged);
        Assert.True(Mathf.Abs(r.HeadYawApplied) < 0.5f);
    }

    [Fact]
    public void Step_release境界のヒステリシス()
    {
        var s = new LookAtState();
        Run(s, 30f, 0f, seconds: 3f);   // engage
        Run(s, 60f, 0f, seconds: 3f);   // 45 < 60 ≤ 65 → engaged 維持
        Assert.True(s.Engaged);
        Run(s, 70f, 0f, seconds: 3f);   // 70 > 65 → release
        Assert.False(s.Engaged);
    }

    [Fact]
    public void Step_release後は正面へ復帰()
    {
        var s = new LookAtState();
        Run(s, 30f, 10f, seconds: 3f);
        var r = Run(s, 90f, 0f, seconds: 5f); // 範囲外 → 0 へ
        // デッドゾーン残差（≤1°）×HeadRatio(0.45) = 最大 0.45° が残る（plan-review 🟡#3）
        Assert.True(Mathf.Abs(r.HeadYawApplied) < 1.0f);
        Assert.True(Mathf.Abs(r.HeadPitchApplied) < 1.0f);
        // 目はデッドゾーンなし＝ほぼ完全にゼロへ
        Assert.True(Mathf.Abs(r.EyeYawApplied) < 0.1f);
    }

    [Fact]
    public void Step_デッドゾーン内の小さなズレには反応しない()
    {
        var s = new LookAtState();
        Run(s, 30f, 0f, seconds: 5f);    // 収束 → Moving=false
        Assert.False(s.Moving);
        float before = s.HeadYaw;
        Run(s, 35f, 0f, seconds: 1f);    // ズレ 5° < 10° → 動かない
        Assert.True(Mathf.Abs(s.HeadYaw - before) < 0.01f);
        Run(s, 45f, 0f, seconds: 1f);    // ズレ 15° ≥ 10° → 動く
        Assert.True(Mathf.Abs(s.HeadYaw - before) > 1f);
    }

    [Fact]
    public void Step_dt補正_30fpsと60fpsで収束途中の角度が一致する()
    {
        // 収束「後」の比較はデッドゾーン凍結点の一致を見るだけで dt 補正を検証できない
        // （再レビュー 🟡#4）→ 収束途中（0.5s 時点・期待値 ≈30×(1-e^-(0.5/0.3))=24.3°）で比較する。
        // 指数平滑が正確なら両者は厳密一致、線形近似バグ（k=dt/tau）なら ≈0.27° ずれて検出される
        var s60 = new LookAtState();
        var s30 = new LookAtState();
        for (int i = 0; i < 30; i++) HeadLookSolver.Step(s60, Ang(30f, 0f), 1f / 60f, in T);
        for (int i = 0; i < 15; i++) HeadLookSolver.Step(s30, Ang(30f, 0f), 1f / 30f, in T);
        Assert.True(s60.HeadYaw > 20f && s60.HeadYaw < 29f); // 収束途中であること
        Assert.True(Mathf.Abs(s60.HeadYaw - s30.HeadYaw) < 0.1f);
    }

    [Fact]
    public void Step_目は首より速く吸着する()
    {
        var s = new LookAtState();
        HeadLookSolver.Step(s, Ang(30f, 0f), 1f / 60f, in T);
        // 1 フレーム後、目の到達割合 > 首の到達割合
        Assert.True(s.EyeYaw / 30f > s.HeadYaw / 30f);
        // ほぼ「1フレで残り半分」: EyeTau=0.025 で 1 フレ到達率 ≈ 49%
        Assert.True(s.EyeYaw > 30f * 0.4f);
    }

    [Fact]
    public void Step_目の適用率は左右20上下10パーセント()
    {
        var s = new LookAtState();
        var r = Run(s, 30f, 20f, seconds: 3f);
        Assert.True(Mathf.Abs(r.EyeYawApplied - 30f * T.EyeYawRatio) < 0.5f);
        Assert.True(Mathf.Abs(r.EyePitchApplied - 20f * T.EyePitchRatio) < 0.5f);
    }

    // ---- Tuning（Config 連携の解決済み値渡し） ----

    /// <summary>カスタム Tuning で n 秒ぶん回す（60fps）。</summary>
    static void RunT(LookAtState s, in HeadLookSolver.Tuning t, float yaw, float pitch, float seconds)
    {
        int n = (int)(seconds * 60f);
        for (int i = 0; i < n; i++)
            HeadLookSolver.Step(s, Ang(yaw, pitch), 1f / 60f, in t);
    }

    [Fact]
    public void Step_Tuningで追従範囲を広げられる()
    {
        var s = new LookAtState();
        var t = T; // struct コピー
        t.EngageYawDeg = 80f;
        RunT(s, in t, 70f, 0f, seconds: 1f); // fixture 45° では範囲外の 70° が、80° 設定なら engage
        Assert.True(s.Engaged);
    }

    [Fact]
    public void Step_releaseはengageプラスマージンで導出される()
    {
        var s = new LookAtState();
        var t = T; // struct コピー
        t.EngageYawDeg = 30f; // → release = 30 + 20 = 50
        RunT(s, in t, 25f, 0f, seconds: 1f);  // 25 ≤ 30 → engage
        Assert.True(s.Engaged);
        RunT(s, in t, 40f, 0f, seconds: 1f);  // 30 < 40 ≤ 50 → 維持（ヒステリシス）
        Assert.True(s.Engaged);
        RunT(s, in t, 55f, 0f, seconds: 1f);  // 55 > 50 → release
        Assert.False(s.Engaged);
    }

    [Fact]
    public void Step_Tuningでデッドゾーンを広げられる()
    {
        var s = new LookAtState();
        var t = T; // struct コピー
        t.DeadZoneStartDeg = 20f;
        RunT(s, in t, 15f, 0f, seconds: 2f); // 15° < 20° → engage はするが首は動かない
        Assert.True(s.Engaged);
        Assert.True(Mathf.Abs(s.HeadYaw) < 0.01f);
    }

    [Fact]
    public void Step_TuningでEyeTauを大きくすると目がゆっくり追従する()
    {
        var s = new LookAtState();
        var t = T; // struct コピー
        t.EyeTau = 0.2f;
        HeadLookSolver.Step(s, Ang(30f, 0f), 1f / 60f, in t);
        Assert.True(s.EyeYaw < 30f * 0.2f); // fixture 0.024 なら 1 フレで ≈50% 進む（≈15°）
    }
}
