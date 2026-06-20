using Xunit;
using BG2VR.Locomotion;

public class GrabMoveStateTests
{
    private const float Dt = 1f / 60f;

    [Fact]
    public void Engage_して移動フレームに入る()
    {
        var s = new GrabMoveState();
        var r = s.Update(true, true, true, false, Dt);   // 左 grip down
        Assert.Equal(GripHand.Left, r.MoveHand);
        Assert.True(r.MarkerResync);                     // engage フレームは marker 取り直しのみ
        r = s.Update(true, true, true, false, Dt);       // 保持
        Assert.Equal(GripHand.Left, r.MoveHand);
        Assert.False(r.MarkerResync);                    // 2 フレーム目から移動適用
        Assert.True(r.LeftBusy);
        Assert.False(r.RightBusy);
    }

    [Fact]
    public void 先勝ち_後から両手で移動凍結()
    {
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        var r = s.Update(true, true, true, true, Dt);    // 右も grip → DualHold
        Assert.Equal(GripHand.None, r.MoveHand);         // 移動凍結（リセット操作中のドリフト防止）
        Assert.True(r.LeftBusy);
        Assert.True(r.RightBusy);
        Assert.False(r.ResetNow);
    }

    [Fact]
    public void 両手1秒でリセット1回_両手離すまで再engage不可()
    {
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        int resetCount = 0;
        for (int i = 0; i < 120; i++)                    // 2 秒両手保持
        {
            var r = s.Update(true, true, true, true, Dt);
            if (r.ResetNow) resetCount++;
        }
        Assert.Equal(1, resetCount);                     // ちょうど 1 回
        var r2 = s.Update(true, true, true, false, Dt);  // 右だけ離す → まだ再 engage しない
        Assert.Equal(GripHand.None, r2.MoveHand);
        Assert.True(r2.LeftBusy);                        // 保持中の手は busy のまま（誤クリック防止）
        s.Update(true, false, true, false, Dt);          // 両手離す → Idle
        var r3 = s.Update(true, true, true, false, Dt);  // 再 engage 可
        Assert.Equal(GripHand.Left, r3.MoveHand);
        Assert.True(r3.MarkerResync);
    }

    [Fact]
    public void DualHold早期解除で残った手が再開_marker再同期()
    {
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        s.Update(true, true, true, true, Dt);            // DualHold
        var r = s.Update(true, true, true, false, Dt);   // 1 秒未満で右を離す
        Assert.Equal(GripHand.Left, r.MoveHand);         // 左で再開
        Assert.True(r.MarkerResync);                     // ジャンプなし
    }

    [Fact]
    public void DualHold中に移動の手を離すと残った手へ引き継ぎ()
    {
        // DualHold からの片手解放は _hand（先勝ちの手）を見ず「残った手」で決まる（spec §4 不変条件）。
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        s.Update(true, true, true, true, Dt);            // DualHold
        var r = s.Update(true, false, true, true, Dt);   // 左（移動の手）を離す
        Assert.Equal(GripHand.Right, r.MoveHand);        // 残った右へ
        Assert.True(r.MarkerResync);
    }

    [Fact]
    public void DualHold中に両手同時に離すとIdle()
    {
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        s.Update(true, true, true, true, Dt);            // DualHold
        var r = s.Update(true, false, true, false, Dt);  // 両手同時 up
        Assert.Equal(GripHand.None, r.MoveHand);
        Assert.False(r.LeftBusy);
        Assert.False(r.RightBusy);
        var r2 = s.Update(true, true, true, false, Dt);  // すぐ再 engage 可（ResetWait ではない）
        Assert.Equal(GripHand.Left, r2.MoveHand);
    }

    [Fact]
    public void リセット到達前フレームで片手を離すとリセットせず再開()
    {
        // 分岐優先順位の確認: 解放フレームは l&&r が崩れているため、タイマーが境界に達していても
        // リセットは発火せず Engaged 再開になる（リセットは「1 秒間握り続けた」場合のみ）。
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        for (int i = 0; i < 59; i++)                     // 59/60 秒両手保持（未発火）
        {
            var rh = s.Update(true, true, true, true, Dt);
            Assert.False(rh.ResetNow);
        }
        var r = s.Update(true, true, true, false, Dt);   // 境界フレームで右を離す
        Assert.False(r.ResetNow);
        Assert.Equal(GripHand.Left, r.MoveHand);
        Assert.True(r.MarkerResync);
    }

    [Fact]
    public void Engaged中に移動の手を離し同フレームで逆手が握っていれば引き継ぎ()
    {
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        var r = s.Update(true, false, true, true, Dt);   // 左 up + 右 down 同フレーム
        Assert.Equal(GripHand.Right, r.MoveHand);
        Assert.True(r.MarkerResync);
    }

    [Fact]
    public void Invalid化で即disengage()
    {
        var s = new GrabMoveState();
        s.Update(true, true, false, false, Dt);          // 左 engage
        var r = s.Update(false, true, false, false, Dt); // 左が invalid（スリープ/切断）
        Assert.Equal(GripHand.None, r.MoveHand);
        Assert.False(r.LeftBusy);
    }

    [Fact]
    public void 同一フレーム両手downは即DualHold()
    {
        var s = new GrabMoveState();
        var r = s.Update(true, true, true, true, Dt);
        Assert.Equal(GripHand.None, r.MoveHand);
        Assert.True(r.LeftBusy);
        Assert.True(r.RightBusy);
    }

    [Fact]
    public void Clearで初期状態に戻る()
    {
        var s = new GrabMoveState();
        s.Update(true, true, true, false, Dt);           // 左 engage
        s.Clear();
        var r = s.Update(true, false, true, false, Dt);  // 何も握っていない
        Assert.Equal(GripHand.None, r.MoveHand);
        Assert.False(r.LeftBusy);
    }
}
