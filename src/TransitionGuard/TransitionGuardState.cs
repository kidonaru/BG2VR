namespace BG2VR.TransitionGuard
{
    /// <summary>
    /// TransitionMonitor が各フレームで実行すべきアクション。
    /// </summary>
    public enum GuardAction
    {
        None,
        Begin,         // VR を teardown する（VRModCore.BeginTransitionGuard）
        End,           // VR の再 attach を許可する（VRModCore.EndTransitionGuard）
        BeginCooldown, // 遷移完了を検出したが re-attach を遅延開始（runner はログのみ。VRModCore は呼ばない）
    }

    /// <summary>
    /// 遷移ガードの純粋状態機械。UnityEngine / BepInEx に非依存（xUnit でテスト可能）。
    ///
    /// 方針（plan §3② / spec §4）:
    ///   - 遷移開始（Harmony Prefix）で <see cref="NotifyStart"/> → 初回は Begin。
    ///   - 完了は IsInputDisabled() の false 復帰をポーリングで検出（Postfix 非依存）。
    ///   - refcount ではなく「ラッチ + 再 arm」で多重/ネスト遷移を 1 回の teardown に集約する。
    ///     新たな遷移開始は _armed を倒し、完了条件を再評価し直す。
    ///   - 入力が一度も disabled にならない no-op 遷移（ChangeScene* 経路の同一遷移早期 return 等。
    ///     showEnvScene は hook 対象外 — TransitionPatches 参照）は
    ///     <see cref="ArmTimeoutSecs"/> 経過で End（= 再 attach。黒画面残り防止）。
    ///   - 想定外に完了を拾えない場合の保険として <see cref="MaxGuardSecs"/> で強制 End。
    /// </summary>
    public sealed class TransitionGuardState
    {
        // 遷移開始後この秒数 input が disabled にならなければ「実遷移ではない」とみなし End する。
        public float ArmTimeoutSecs = 1.0f;
        // 完了を拾えないまま input が enabled で居続けた場合の保険上限（input disabled 中は作動しない）。
        public float MaxGuardSecs = 30.0f;

        private bool _guarding;
        private bool _armed;          // input が disabled になった（= 実遷移が始まった）のを観測済み
        private bool _coolingDown;    // 遷移完了済み・re-attach 遅延中（案 F: ロードバースト回避）
        private float _guardStartTime;
        private float _lastStartTime; // 直近の NotifyStart 時刻（再 arm の起点）
        private float _cooldownStartTime;

        public bool IsGuarding => _guarding;
        public bool IsArmed => _armed;
        public bool IsCoolingDown => _coolingDown;

        /// <summary>遷移開始の通知（Harmony Prefix から）。初回ガード開始時のみ Begin を返す。</summary>
        public GuardAction NotifyStart(float now)
        {
            if (!_guarding)
            {
                _guarding = true;
                _armed = false;
                _coolingDown = false;
                _guardStartTime = now;
                _lastStartTime = now;
                return GuardAction.Begin;
            }

            // 既にガード中: 新たな遷移が重なったので完了検出を再 arm（teardown は冪等なので Begin 不要）。
            // cooldown 中なら破棄＝新遷移の完了検出（ArmTimeout 含む）が再び支配する。
            _armed = false;
            _coolingDown = false;
            _lastStartTime = now;
            return GuardAction.None;
        }

        /// <summary>
        /// fade-in 開始の通知（Runner が ScreenFade の IN_FADEIN への edge で呼ぶ・本 plan 2026-06-19）。
        /// guarding かつ armed（= 実遷移が input disabled を観測済み）なら即 End して再 attach を許可する。
        ///
        /// なぜ input disabled 中でも End して安全か: IN_FADEIN は全 ChangeScene* 経路で
        /// load/unload/Resources.UnloadUnusedAssets の await 完了**後**にしか await されない。
        /// よって fade-in 開始時点では「UnloadUnusedAssets 中に eye カメラが描画」（フリーズ根本原因）の
        /// 危険窓は既に閉じている。再 attach 後 eye が新シーンを描き、fade-in がそれを露出する
        /// （= fade-in が旧シーンの凍結フレームを映す不具合の解消）。armed 必須＝実遷移以外では誤 End しない。
        /// </summary>
        public GuardAction NotifyFadeInStarted()
        {
            if (!_guarding || !_armed) return GuardAction.None;
            EndGuard();
            return GuardAction.End;
        }

        /// <summary>
        /// 毎フレーム評価。完了/タイムアウトを検出したら End を返す。
        /// reattachDelaySecs は解決済み config 値を引数で受ける（0 以下 = 遅延なし＝従来動作）。
        /// </summary>
        public GuardAction Tick(bool inputDisabled, float now, float reattachDelaySecs)
        {
            if (!_guarding) return GuardAction.None;

            if (inputDisabled)
            {
                // 実遷移が engage 中。input が disabled の間は絶対に End しない。
                // ここで End すると fork が即再 attach → unload 中に eye カメラ復活でデッドロックが
                // 再発する（= フリーズ根治が最優先。MaxGuard 保険より遷移未完了の保護を優先する）。
                // cooldown 中の再 disabled は新たなロード活動とみなし cooldown を破棄して armed へ戻す
                //（完了後に delay をフル数え直すのが意図動作）。
                _armed = true;
                _coolingDown = false;
                return GuardAction.None;
            }

            // ここ以降は input enabled。End は必ず input enabled 時のみ起きる（disabled 中の rig 復活を防ぐ）。
            if (_armed)
            {
                // disabled → enabled に戻った = 遷移完了。
                if (reattachDelaySecs <= 0f)
                {
                    // 遅延なし（既定）= 従来動作で即 re-attach
                    EndGuard();
                    return GuardAction.End;
                }
                // 案 F: 遷移直後のロード GPU バーストと eye render の重複を避けるため re-attach を遅延
                _armed = false;
                _coolingDown = true;
                _cooldownStartTime = now;
                return GuardAction.BeginCooldown;
            }

            if (_coolingDown)
            {
                // 不変条件: この分岐は必ず early-return し、下の ArmTimeout フォールスルーへ到達させない。
                // _lastStartTime（遷移開始時刻）は cooldown 開始より必ず古いため、到達すると
                // ArmTimeoutSecs（1s）で即 End＝遅延がサイレント無効化される。
                // MaxGuard 保険は cooldown 中も生かす（inputDisabled フリッカによる無限延長の上限保証）。
                if (now - _cooldownStartTime >= reattachDelaySecs || now - _guardStartTime > MaxGuardSecs)
                {
                    EndGuard();
                    return GuardAction.End;
                }
                return GuardAction.None;
            }

            // 一度も disabled にならない no-op 遷移（早期 return / Hole 分岐）はタイムアウトで End（再 attach）。
            // 完了を拾えないまま enabled が続く場合の保険（MaxGuard）も input enabled 時のみ作動させる。
            if (now - _lastStartTime > ArmTimeoutSecs || now - _guardStartTime > MaxGuardSecs)
            {
                EndGuard();
                return GuardAction.End;
            }

            return GuardAction.None;
        }

        // End 後はフラグ全クリアを不変条件にする（End パス間でクリーンアップを一元化。
        // _guarding=false だけに畳む暗黙依存を排し、IsArmed/IsCoolingDown の stale 残留を防ぐ）
        private void EndGuard()
        {
            _guarding = false;
            _armed = false;
            _coolingDown = false;
        }
    }
}
