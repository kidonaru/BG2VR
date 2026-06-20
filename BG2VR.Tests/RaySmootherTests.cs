using BG2VR.VrInput;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests;

public class RaySmootherTests
{
    // τ=0 はパススルー（スムージング無効）。
    [Fact]
    public void Zero_tau_passes_through()
    {
        var s = new RaySmoother();
        s.Update(new Vector3(1, 2, 3), Vector3.right, 0.016f, 0f, out var o, out var d);
        Assert.Equal(new Vector3(1, 2, 3), o);
        Assert.Equal(Vector3.right, d);
    }

    // 初回は raw をそのまま採用（ゼロ初期値からの引っ張り無し）。
    [Fact]
    public void First_update_snaps_to_raw()
    {
        var s = new RaySmoother();
        s.Update(new Vector3(5, 0, 0), Vector3.forward, 0.016f, 0.1f, out var o, out var d);
        Assert.Equal(new Vector3(5, 0, 0), o);
        Assert.Equal(Vector3.forward, d);
    }

    // 同一 raw を入れ続けると raw へ収束する。
    [Fact]
    public void Converges_to_steady_input()
    {
        var s = new RaySmoother();
        s.Update(Vector3.zero, Vector3.forward, 0.016f, 0.07f, out _, out _);
        Vector3 target = new Vector3(1, 0, 0);
        Vector3 o = default, d = default;
        for (int i = 0; i < 300; i++)
            s.Update(target, Vector3.right, 0.016f, 0.07f, out o, out d);
        Assert.True((o - target).magnitude < 1e-3f);
        Assert.True((d - Vector3.right).magnitude < 1e-3f);
    }

    // dt 補正: dt を 1 回 ≒ dt/2 を 2 回（フレームレート非依存）。
    [Fact]
    public void Dt_correction_is_frame_rate_independent()
    {
        var a = new RaySmoother();
        var b = new RaySmoother();
        a.Update(Vector3.zero, Vector3.forward, 0.016f, 0.1f, out _, out _);
        b.Update(Vector3.zero, Vector3.forward, 0.016f, 0.1f, out _, out _);
        Vector3 t = new Vector3(1, 0, 0);
        a.Update(t, Vector3.forward, 0.032f, 0.1f, out var oa, out _);
        b.Update(t, Vector3.forward, 0.016f, 0.1f, out _, out _);
        b.Update(t, Vector3.forward, 0.016f, 0.1f, out var ob, out _);
        Assert.True((oa - ob).magnitude < 1e-4f);
    }

    // dir は常に正規化されて返る。
    [Fact]
    public void Dir_stays_normalized()
    {
        var s = new RaySmoother();
        s.Update(Vector3.zero, Vector3.forward, 0.016f, 0.1f, out _, out _);
        s.Update(Vector3.zero, (Vector3.forward + Vector3.right).normalized, 0.016f, 0.1f, out _, out var d);
        Assert.Equal(1f, d.magnitude, 3);
    }

    // Reset 後は次の入力に snap する（古い pose への引っ張り防止）。
    [Fact]
    public void Reset_snaps_to_next_input()
    {
        var s = new RaySmoother();
        s.Update(Vector3.zero, Vector3.forward, 0.016f, 0.1f, out _, out _);
        s.Reset();
        s.Update(new Vector3(9, 9, 9), Vector3.up, 0.016f, 0.1f, out var o, out var d);
        Assert.Equal(new Vector3(9, 9, 9), o);
        Assert.Equal(Vector3.up, d);
    }
}
