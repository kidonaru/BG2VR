using UnityEngine;
using Xunit;
using BG2VR.HandSumoPush;

public class HandSumoPushDetectorTests
{
    // Smoothing=1（平滑なし）でテストを決定的化。yaml 既定値とは独立（CLAUDE.md 規約）。
    private static HandSumoPushDetector.Params P() => new HandSumoPushDetector.Params
    {
        High = 0.7f,
        ReleaseRatio = 0.5f,
        RefractorySec = 0.3f,
        CoincidenceSec = 0.25f,
        Smoothing = 1f,
    };

    // 速度系列を流して発火回数を返す。seed フレーム（zero）を必ず先頭に置く。
    private static int CountFires(HandSumoPushDetector.Params p, float dt, params (Vector3 left, Vector3 right)[] frames)
    {
        var s = HandSumoPushDetector.NewState();
        int fires = 0;
        foreach (var f in frames)
            if (HandSumoPushDetector.Step(ref s, f.left, f.right, dt, p)) fires++;
        return fires;
    }

    private static Vector3 Fwd(float v) => new Vector3(0f, 0f, v);
    private static readonly Vector3 Zero = Vector3.zero;

    [Fact]
    public void FirstFrame_SeedsOnly_NoFire()
    {
        var s = HandSumoPushDetector.NewState();
        bool fire = HandSumoPushDetector.Step(ref s, Fwd(2f), Fwd(2f), 0.1f, P());
        Assert.False(fire);
    }

    [Fact]
    public void BothHandsForward_FiresOnce()
    {
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Fwd(2f)),
            (Fwd(2f), Fwd(2f)),
            (Fwd(2f), Fwd(2f)),
            (Fwd(2f), Fwd(2f)),
            (Fwd(2f), Fwd(2f)));
        Assert.Equal(1, fires); // 押しっぱなしでも 1 発（不応期）
    }

    [Fact]
    public void OneHandOnly_NoFire()
    {
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Zero),
            (Fwd(2f), Zero),
            (Fwd(2f), Zero));
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Coincidence_WithinWindow_Fires()
    {
        // 左→（窓 0.25s 内に）右 の順で前進＝同時性窓内 → 発火。
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Zero),       // 左のみ（LeftPushTimer=0.25）
            (Zero, Fwd(2f)));      // 右のみ（dt=0.1 後＝LeftPushTimer=0.15>0）→ 両手 timer 生存
        Assert.Equal(1, fires);
    }

    [Fact]
    public void Coincidence_OutsideWindow_NoFire()
    {
        // 左前進 → 窓超（0.3s 経過）→ 右前進 ＝ 左 timer 失効済み → 不発。
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Zero),       // 左のみ（timer=0.25）
            (Zero, Zero),          // 0.15
            (Zero, Zero),          // 0.05
            (Zero, Zero),          // 0（失効）
            (Zero, Fwd(2f)));      // 右のみ → 左 timer=0 → 不発
        Assert.Equal(0, fires);
    }

    [Fact]
    public void ReArm_AfterRelease_FiresAgain()
    {
        // 両手前進(発火) → 両手停止で再武装 → 再度両手前進(発火) ＝ 2 発。
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Fwd(2f)),    // fire1, refractory=0.3
            (Zero, Zero),          // refr ~0.2
            (Zero, Zero),          // refr ~0.1
            (Zero, Zero),          // refr ~0（float 残差で僅かに正＝この frame ではまだ再武装しない）
            (Zero, Zero),          // refr 0 確定 → 再武装
            (Fwd(2f), Fwd(2f)));   // fire2
        Assert.Equal(2, fires);
    }

    [Fact]
    public void SustainedPush_ThenRelease_FiresOnce()
    {
        // 長押し（発火後も両手を High 超で保持）→ 解放 の系列で 1 発のみ。
        // 旧実装は再武装時に push timer を残すため、解放 tail の残存窓で 2 発目が誤発火していた（回帰固定）。
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Fwd(2f)),    // fire1（refr=0.3）
            (Fwd(2f), Fwd(2f)),    // 押下継続（refr 0.2・両 timer=0.25 に張り直し）
            (Fwd(2f), Fwd(2f)),    // refr 0.1
            (Fwd(2f), Fwd(2f)),    // refr 0
            (Fwd(2f), Fwd(2f)),    // refr 0（push 継続で再武装しない・両 timer=0.25）
            (Zero, Zero),          // 解放 → 不応期明け & 両手減速で再武装（push timer をクリア）
            (Zero, Zero));         // 旧: 残存 timer で fire2 / 新: timer=0 のため不発
        Assert.Equal(1, fires);
    }

    [Fact]
    public void OneHandReleased_OtherHandTapsRepeatedly_NoSecondFire_AtDefaultTiming()
    {
        // docstring の不変条件を固定: 既定 CoincidenceSec(0.25) ≤ RefractorySec(0.3) では、1 回の両手発火後に
        // 片手(左)を戻し反対手(右)を連打しても 2 発目は出ない（左の push timer は再武装=不応期明け より先に失効）。
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),          // seed
            (Fwd(2f), Fwd(2f)),    // fire1（refr=0.3・両 timer=0.25）
            (Zero, Fwd(2f)),       // 左 release・右 tap（左 timer 減衰開始）
            (Zero, Zero),
            (Zero, Zero),          // refr 明け & 左 timer 失効 → ここで再武装
            (Zero, Fwd(2f)));      // 右 tap → 左 timer=0 のため AND 不成立 → 不発
        Assert.Equal(1, fires);
    }

    [Fact]
    public void BackwardOrUpward_NoFire()
    {
        int fires = CountFires(P(), 0.1f,
            (Zero, Zero),                              // seed
            (new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, -2f)),  // 後方
            (new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, 0f)));   // 上方
        Assert.Equal(0, fires);
    }
}
