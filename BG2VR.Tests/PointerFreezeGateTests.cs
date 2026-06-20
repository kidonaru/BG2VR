using BG2VR.VrInput;
using Xunit;

namespace BG2VR.Tests;

public class PointerFreezeGateTests
{
    private const float Dt = 0.016f;

    // テスト用しきい値で呼ぶヘルパー（Configs の default とは独立。gate は解決済み値を引数で受けるだけ）。
    private static PointerFreezeGate.Result Up(
        PointerFreezeGate g, float v,
        float recover = 0.15f, float timeout = 1.0f,
        float onset = 0.10f, float release = 0.05f, float dt = Dt)
        => g.Update(v, dt, onset, release, timeout, recover);

    // ONSET 未満では凍結しない（live=Blend 1）。
    [Fact]
    public void Below_onset_stays_idle()
    {
        var g = new PointerFreezeGate();
        var r = Up(g, 0.05f);
        Assert.False(r.Frozen);
        Assert.False(r.JustFroze);
        Assert.Equal(1f, r.Blend);
    }

    // ONSET 到達で凍結開始。JustFroze は最初の 1 フレームのみ・凍結中は Blend 0。
    [Fact]
    public void Onset_freezes_with_justfroze_once()
    {
        var g = new PointerFreezeGate();
        var r1 = Up(g, 0.2f);
        Assert.True(r1.Frozen);
        Assert.True(r1.JustFroze);
        Assert.Equal(0f, r1.Blend);
        var r2 = Up(g, 0.9f);
        Assert.True(r2.Frozen);
        Assert.False(r2.JustFroze);
        Assert.Equal(0f, r2.Blend);
    }

    // RELEASE 以下で Recovering 開始: 初回 Blend は小さく、単調増加して 1 で Idle 到達。
    [Fact]
    public void Release_starts_smooth_recovery()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.5f); // 凍結
        var r = Up(g, 0.0f);
        Assert.False(r.Frozen);
        Assert.True(r.Blend > 0f && r.Blend < 0.1f); // smoothstep(0.016/0.15)≈0.03

        float prev = r.Blend;
        bool reachedOne = false;
        for (int i = 0; i < 20; i++)
        {
            var ri = Up(g, 0.0f);
            Assert.True(ri.Blend >= prev); // 単調増加
            prev = ri.Blend;
            if (ri.Blend >= 1f) { reachedOne = true; break; }
        }
        Assert.True(reachedOne); // 0.15s ≒ 10 フレームで復帰完了
    }

    // onset と release の間（ヒステリシス帯）では凍結維持。
    [Fact]
    public void Hysteresis_band_keeps_frozen()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.5f);
        var r = Up(g, 0.07f); // release(0.05) < 0.07 < onset(0.10)
        Assert.True(r.Frozen);
    }

    // タイムアウトで解除（recover=0 は即時復帰=旧ワープ挙動）。
    // 押しっぱなしでは再凍結せず、一度 RELEASE 以下に戻すと再武装される。
    [Fact]
    public void Timeout_disarms_until_release()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.9f, recover: 0f); // 凍結開始
        for (int i = 0; i < 70; i++) Up(g, 0.9f, recover: 0f); // 70*0.016=1.12s > 1.0s
        var held = Up(g, 0.9f, recover: 0f);
        Assert.False(held.Frozen);      // タイムアウト済
        Assert.Equal(1f, held.Blend);   // recover=0 ＝即時 live
        var stillHeld = Up(g, 0.9f, recover: 0f);
        Assert.False(stillHeld.Frozen); // 押しっぱなしでは再凍結しない（disarm）
        Assert.False(stillHeld.JustFroze);
        Up(g, 0.0f, recover: 0f);       // 離す → 再武装
        var again = Up(g, 0.9f, recover: 0f);
        Assert.True(again.Frozen);
        Assert.True(again.JustFroze);
    }

    // timeout=0 はタイムアウト無効＝押し続ける限り凍結維持。
    [Fact]
    public void Timeout_zero_never_expires()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.9f, timeout: 0f);
        PointerFreezeGate.Result r = default;
        for (int i = 0; i < 200; i++) r = Up(g, 0.9f, timeout: 0f); // 3.2s 押しっぱなし
        Assert.True(r.Frozen);
    }

    // recover=0 は解除で即時復帰（旧ワープ挙動）。
    [Fact]
    public void Recover_zero_warps()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.5f, recover: 0f);
        var r = Up(g, 0.0f, recover: 0f);
        Assert.False(r.Frozen);
        Assert.Equal(1f, r.Blend);
    }

    // 復帰途中の再押し込み＝即再凍結（JustFroze + Blend=0 が同時成立・runner は live を再 latch）。
    [Fact]
    public void Refreeze_during_recovery()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.5f);            // 凍結
        var rec = Up(g, 0.0f);  // Recovering（blend 途中）
        Assert.True(rec.Blend > 0f && rec.Blend < 1f);
        var r = Up(g, 0.5f);    // 復帰途中で再押し込み
        Assert.True(r.Frozen);
        Assert.True(r.JustFroze);
        Assert.Equal(0f, r.Blend);
    }

    // タイムアウト→押しっぱなしのまま Recovering 完走→Idle 後も disarm が保持され、
    // 再凍結せず live(Blend=1) を返し続ける（旧 Expired に無かった新経路）。
    [Fact]
    public void Disarmed_idle_returns_live()
    {
        var g = new PointerFreezeGate();
        // timeout 0.05s / recover 0.03s ＝数フレームで timeout→復帰完走する設定
        for (int i = 0; i < 20; i++) Up(g, 0.9f, recover: 0.03f, timeout: 0.05f);
        var r = Up(g, 0.9f, recover: 0.03f, timeout: 0.05f);
        Assert.False(r.Frozen);
        Assert.False(r.JustFroze);
        Assert.Equal(1f, r.Blend);
    }

    // release > onset の設定ミスは onset に丸められ、凍結→即解除のチャタりが起きない。
    [Fact]
    public void Release_above_onset_is_clamped()
    {
        var g = new PointerFreezeGate();
        var r1 = Up(g, 0.2f, release: 0.3f); // onset=0.10 / release=0.30(>onset)
        Assert.True(r1.JustFroze);
        var r2 = Up(g, 0.2f, release: 0.3f); // 丸め後 release=0.10 < 0.2 → 凍結維持
        Assert.True(r2.Frozen);
        Assert.False(r2.JustFroze);
    }

    // Reset で Idle + disarm 解除（teardown / snap invalid 時用）。
    [Fact]
    public void Reset_returns_to_idle_and_rearms()
    {
        var g = new PointerFreezeGate();
        Up(g, 0.9f, recover: 0f);
        for (int i = 0; i < 70; i++) Up(g, 0.9f, recover: 0f); // timeout → disarm
        g.Reset();
        var r = Up(g, 0.9f, recover: 0f);
        Assert.True(r.JustFroze); // disarm も解除されている
    }
}
