using UnityEngine;
using Xunit;
using BG2VR.KaraokeShake;

public class KaraokeShakeDetectorTests
{
    // Smoothing=1（平滑なし）でテストを決定的化。yaml 既定値とは独立（CLAUDE.md 規約）。
    private static KaraokeShakeDetector.Params P() => new KaraokeShakeDetector.Params
    {
        High = 2f,
        ReleaseRatio = 0.5f,
        RefractorySec = 0.1f,
        DownWeight = 1f,
        ForwardWeight = 1f,
        AngularWeight = 0.1f,
        LiftVetoSpeed = 0.3f,
        Smoothing = 1f,
    };

    // 速度系列を流して発火回数を返す。seed フレーム（zero）を必ず先頭に置く。
    private static int CountFires(KaraokeShakeDetector.Params p, float dt, params (Vector3 lin, float ang)[] frames)
    {
        var s = KaraokeShakeDetector.NewState();
        int fires = 0;
        foreach (var f in frames)
            if (KaraokeShakeDetector.Step(ref s, f.lin, f.ang, dt, p)) fires++;
        return fires;
    }

    [Fact]
    public void FirstFrame_SeedsOnly_NoFire()
    {
        var s = KaraokeShakeDetector.NewState();
        bool fire = KaraokeShakeDetector.Step(ref s, new Vector3(0f, -5f, 0f), 0f, 0.1f, P());
        Assert.False(fire);
    }

    [Fact]
    public void Lift_Upward_NoFire()
    {
        // 上向き運動（持ち上げ）を 5 フレーム。下/前項 0・角速度 0 → 不発。
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (new Vector3(0f, 5f, 0f), 0f),
            (new Vector3(0f, 5f, 0f), 0f),
            (new Vector3(0f, 5f, 0f), 0f),
            (new Vector3(0f, 5f, 0f), 0f));
        Assert.Equal(0, fires);
    }

    [Fact]
    public void DownStrike_FiresOnce()
    {
        // seed → 下振り 1 発。score = 5 ≥ 2。
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (new Vector3(0f, -5f, 0f), 0f));
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ForwardJab_FiresOnce()
    {
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (new Vector3(0f, 0f, 5f), 0f));
        Assert.Equal(1, fires);
    }

    [Fact]
    public void WristSnap_AngularOnly_Fires()
    {
        // 線速度ほぼゼロ・角速度 30 rad/s → score = 30*0.1 = 3 ≥ 2。
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (Vector3.zero, 30f));
        Assert.Equal(1, fires);
    }

    [Fact]
    public void WristSnap_WhileLifting_NoFire()
    {
        // 持ち上げ(上向き 5m/s > LiftVetoSpeed 0.3)中の手首スナップ(角速度 30)→ 角速度項が無効化され不発。
        // 「持ち上げで反応しない」要件の角速度経路を保証する（plan-review 🔴3）。
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (new Vector3(0f, 5f, 0f), 30f),
            (new Vector3(0f, 5f, 0f), 30f));
        Assert.Equal(0, fires);
    }

    [Fact]
    public void SustainedHigh_FiresOnceUntilRearm()
    {
        // 下振りが 5 フレーム張り付き → 発火は 1 回のみ（再武装は低スコアが必要）。
        int fires = CountFires(P(), 0.01f,
            (Vector3.zero, 0f),
            (new Vector3(0f, -5f, 0f), 0f),
            (new Vector3(0f, -5f, 0f), 0f),
            (new Vector3(0f, -5f, 0f), 0f),
            (new Vector3(0f, -5f, 0f), 0f),
            (new Vector3(0f, -5f, 0f), 0f));
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ReleaseThenSecondStrike_FiresTwice()
    {
        // 下振り→ゼロ（不応期 0.1 を dt0.1 で消化＋低スコアで再武装）→下振り。
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (new Vector3(0f, -5f, 0f), 0f),  // fire1, timer=0.1
            (Vector3.zero, 0f),              // timer→0, score0≤1 → 再武装
            (new Vector3(0f, -5f, 0f), 0f)); // fire2
        Assert.Equal(2, fires);
    }

    [Fact]
    public void Refractory_BlocksRapidDouble()
    {
        // 発火直後に dt0.01 で再度高スコア → 不応期(0.1)残存で抑止。
        int fires = CountFires(P(), 0.01f,
            (Vector3.zero, 0f),
            (new Vector3(0f, -5f, 0f), 0f),  // fire1, timer=0.1
            (new Vector3(0f, -5f, 0f), 0f),  // timer=0.09, Armed=false → 抑止
            (new Vector3(0f, -5f, 0f), 0f)); // timer=0.08 → 抑止
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Backward_NoFire()
    {
        // 後ろ振り（-z）・y=0 → 下/前項 0 → 不発。
        int fires = CountFires(P(), 0.1f,
            (Vector3.zero, 0f),
            (new Vector3(0f, 0f, -5f), 0f),
            (new Vector3(0f, 0f, -5f), 0f));
        Assert.Equal(0, fires);
    }
}
