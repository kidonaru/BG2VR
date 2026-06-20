using BG2VR.TransitionGuard;
using Xunit;

namespace BG2VR.Tests
{
    public class TransitionGuardStateTests
    {
        [Fact]
        public void NotifyStart_初回はBegin_ガード中の再通知はNone()
        {
            var s = new TransitionGuardState();
            Assert.Equal(GuardAction.Begin, s.NotifyStart(0f));
            Assert.True(s.IsGuarding);
            // ガード中の再通知は teardown 冪等なので Begin を返さない
            Assert.Equal(GuardAction.None, s.NotifyStart(0.1f));
        }

        [Fact]
        public void Tick_ガード前はNone()
        {
            var s = new TransitionGuardState();
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 0f, reattachDelaySecs: 0f));
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 1f, reattachDelaySecs: 0f));
        }

        [Fact]
        public void 通常遷移_disabled後にenabledへ戻るとEnd()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            // 遷移が engage（input disabled）
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 0f));
            Assert.True(s.IsArmed);
            // input が戻る = 完了
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 0.5f, reattachDelaySecs: 0f));
            Assert.False(s.IsGuarding);
            // End 後は再びガード前と同じ挙動
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 0.6f, reattachDelaySecs: 0f));
        }

        [Fact]
        public void NoOp遷移_一度もdisabledにならなければArmTimeoutでEnd()
        {
            var s = new TransitionGuardState { ArmTimeoutSecs = 1.0f };
            s.NotifyStart(0f);
            // タイムアウト前は保留（早期 return / Hole 分岐で input が disabled にならないケース）
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 0.5f, reattachDelaySecs: 0f));
            Assert.True(s.IsGuarding);
            // タイムアウト超過で再 attach（黒画面残り防止）
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 1.1f, reattachDelaySecs: 0f));
            Assert.False(s.IsGuarding);
        }

        [Fact]
        public void 多重遷移_再armされ完了は一度だけEnd()
        {
            var s = new TransitionGuardState();
            Assert.Equal(GuardAction.Begin, s.NotifyStart(0f));
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 0f)); // armed
            // 遷移が重なる → 再 arm（Begin は返さない）
            Assert.Equal(GuardAction.None, s.NotifyStart(0.2f));
            Assert.False(s.IsArmed);
            // 重なった遷移が engage → 完了
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 0.3f, reattachDelaySecs: 0f));
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 0.4f, reattachDelaySecs: 0f));
            Assert.False(s.IsGuarding);
        }

        [Fact]
        public void 再arm後_完了前に入力が戻ってもEndしない理由の確認_armが必要()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 0f); // armed
            s.NotifyStart(0.2f);                                          // 再 arm（armed=false）
            // まだ disabled を観測していないので、即 enabled でも完了とはみなさない（タイムアウト未満）
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 0.3f, reattachDelaySecs: 0f));
            Assert.True(s.IsGuarding);
        }

        [Fact]
        public void input_disabled中はMaxGuard超過でもEndしない_デッドロック回避優先()
        {
            var s = new TransitionGuardState { MaxGuardSecs = 30f };
            s.NotifyStart(0f);
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 10f, reattachDelaySecs: 0f)); // armed
            // input が disabled の間は MaxGuard を超えても End しない
            // （End→fork が rig 復活→unload 中デッドロック再発を防ぐ）。
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 31f, reattachDelaySecs: 0f));
            Assert.True(s.IsGuarding);
            // input が enabled に戻れば End する
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 32f, reattachDelaySecs: 0f));
            Assert.False(s.IsGuarding);
        }

        // ── TransitionReattachDelaySec（案 F: re-attach 遅延・freeze/Link 切断切り分け knob）──

        [Fact]
        public void 遅延あり_完了でBeginCooldown_経過後にEnd()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 2f); // armed
            // 完了 → 即 End ではなく cooldown 開始
            Assert.Equal(GuardAction.BeginCooldown, s.Tick(inputDisabled: false, now: 0.5f, reattachDelaySecs: 2f));
            Assert.True(s.IsCoolingDown);
            Assert.True(s.IsGuarding);
            // 経過前は None
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 1.5f, reattachDelaySecs: 2f));
            // delay 経過で End
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 2.5f, reattachDelaySecs: 2f));
            Assert.False(s.IsGuarding);
            Assert.False(s.IsCoolingDown);
        }

        [Fact]
        public void Cooldown中はArmTimeoutフォールスルーに到達しない()
        {
            // 不変条件（plan-review major）: _lastStartTime は cooldown 開始より古いため、
            // フォールスルーに到達すると ArmTimeout(1s) で即 End＝遅延がサイレント無効化される。
            var s = new TransitionGuardState { ArmTimeoutSecs = 1.0f };
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 3f);
            Assert.Equal(GuardAction.BeginCooldown, s.Tick(inputDisabled: false, now: 0.2f, reattachDelaySecs: 3f));
            // cooldown 開始から 1.5s（_lastStartTime からは 1.7s ＝ ArmTimeout 超過済み）でも End しない
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 1.7f, reattachDelaySecs: 3f));
            Assert.True(s.IsGuarding);
            // delay 3s 経過で End
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 3.3f, reattachDelaySecs: 3f));
        }

        [Fact]
        public void Cooldown中のdisabled再観測でarmedへ戻りdelayをフル数え直し()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 2f);
            Assert.Equal(GuardAction.BeginCooldown, s.Tick(inputDisabled: false, now: 0.5f, reattachDelaySecs: 2f));
            // ロード活動が再 engage → cooldown 破棄して armed（意図動作: フリッカ 1 回ごとにフル再カウント）
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: true, now: 1.0f, reattachDelaySecs: 2f));
            Assert.False(s.IsCoolingDown);
            Assert.True(s.IsArmed);
            // 完了 → cooldown 数え直し
            Assert.Equal(GuardAction.BeginCooldown, s.Tick(inputDisabled: false, now: 1.5f, reattachDelaySecs: 2f));
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 3.0f, reattachDelaySecs: 2f)); // 1.5s 経過のみ
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 3.6f, reattachDelaySecs: 2f));
        }

        [Fact]
        public void Cooldown中のNotifyStartでcooldown破棄_即タイムアウトしない()
        {
            var s = new TransitionGuardState { ArmTimeoutSecs = 1.0f };
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 2f);
            s.Tick(inputDisabled: false, now: 0.5f, reattachDelaySecs: 2f); // BeginCooldown
            // 新遷移が cooldown 中に来た → cooldown 破棄・_lastStartTime リセット
            Assert.Equal(GuardAction.None, s.NotifyStart(5.0f));
            Assert.False(s.IsCoolingDown);
            // _lastStartTime=5.0 起点で ArmTimeout を数えるため即タイムアウトしない
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 5.5f, reattachDelaySecs: 2f));
            Assert.True(s.IsGuarding);
            // 新遷移が no-op のままなら ArmTimeout で End（既存挙動）
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 6.1f, reattachDelaySecs: 2f));
        }

        [Fact]
        public void Cooldown中もMaxGuard保険が効く()
        {
            var s = new TransitionGuardState { MaxGuardSecs = 30f };
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 29f, reattachDelaySecs: 10f);
            Assert.Equal(GuardAction.BeginCooldown, s.Tick(inputDisabled: false, now: 29.5f, reattachDelaySecs: 10f));
            // delay 10s が残っていても MaxGuard 30s 超過で強制 End（フリッカ無限延長の上限保証）
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 31f, reattachDelaySecs: 10f));
            Assert.False(s.IsGuarding);
        }

        [Fact]
        public void 遅延10_MaxGuard30の正常順序_delay到達が先にEndする()
        {
            // config レンジ（delay≤10 < MaxGuard=30）の正常系を固定（将来 MaxGuard を縮めた場合の退行検出）
            var s = new TransitionGuardState { MaxGuardSecs = 30f };
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 10f);
            Assert.Equal(GuardAction.BeginCooldown, s.Tick(inputDisabled: false, now: 0.5f, reattachDelaySecs: 10f));
            Assert.Equal(GuardAction.None, s.Tick(inputDisabled: false, now: 9f, reattachDelaySecs: 10f));
            Assert.Equal(GuardAction.End, s.Tick(inputDisabled: false, now: 10.5f, reattachDelaySecs: 10f));
        }

        // ── NotifyFadeInStarted（fade-in 先行復帰・本 plan 2026-06-19）──

        [Fact]
        public void NotifyFadeInStarted_armed遷移中はinputDisabledでもEndする()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            // 遷移 engage（input disabled）で armed
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 0f);
            Assert.True(s.IsArmed);
            // fade-in 開始 = まだ input disabled でも即 End
            //（IN_FADEIN は load/UnloadUnusedAssets 完了後にしか来ない＝再 attach 安全）
            Assert.Equal(GuardAction.End, s.NotifyFadeInStarted());
            Assert.False(s.IsGuarding);
            Assert.False(s.IsArmed);
        }

        [Fact]
        public void NotifyFadeInStarted_armed前は無視()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            // まだ input disabled を観測していない（armed=false）→ no-op（誤 End しない）
            Assert.Equal(GuardAction.None, s.NotifyFadeInStarted());
            Assert.True(s.IsGuarding);
        }

        [Fact]
        public void NotifyFadeInStarted_ガード外は無視()
        {
            var s = new TransitionGuardState();
            Assert.Equal(GuardAction.None, s.NotifyFadeInStarted());
            Assert.False(s.IsGuarding);
        }

        [Fact]
        public void NotifyFadeInStarted_End後の二度目はNone_冪等()
        {
            var s = new TransitionGuardState();
            s.NotifyStart(0f);
            s.Tick(inputDisabled: true, now: 0.1f, reattachDelaySecs: 0f);
            Assert.Equal(GuardAction.End, s.NotifyFadeInStarted());
            // 同一 IN_FADEIN 区間で edge 検出をすり抜けて再呼出されても guarding=false で None
            Assert.Equal(GuardAction.None, s.NotifyFadeInStarted());
        }
    }
}
