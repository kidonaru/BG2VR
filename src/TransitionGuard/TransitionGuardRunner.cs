using System;
using UnityEngine;
using UnityVRMod.Core;
using GB;

namespace BG2VR.TransitionGuard
{
    /// <summary>
    /// 遷移ガードの実行体（MonoBehaviour）。
    ///
    /// - Harmony Prefix（<see cref="TransitionPatches"/>）が遷移開始時に <see cref="NotifyTransitionStart"/> を呼ぶ
    ///   → 純状態機械が初回のみ Begin を返し、VR を真に teardown する（フリーズ根治）。
    /// - 毎フレーム <see cref="GBSystem.IsInputDisabled"/> をポーリングして完了を検出し、再 attach を許可する。
    ///
    /// VRModCore.Begin/EndTransitionGuard は冪等・null セーフなので VR 無効時に呼んでも無害。
    /// </summary>
    public sealed class TransitionGuardRunner : MonoBehaviour
    {
        public static TransitionGuardRunner Instance { get; private set; }

        /// <summary>遷移完了で再 attach を許可した瞬間に発火（CameraBridge 等の再解決トリガに使える）。</summary>
        public static event Action TransitionEnded;

        private readonly TransitionGuardState _state = new TransitionGuardState();

        // ゲーム ScreenFade(m_fade) の状態を System.IObserver で受けるラッチ。
        // UniRx 参照を避けるため自前実装（OnStateChanged() の戻り値は System.IObservable<FadeState>）。
        // UniRx ReactiveProperty は購読時に現在値を即 push し、以後の状態変化ごとに OnNext する＝ポーリング不要。
        // ラッチは「最新値のみ」保持する。1 フレーム内に FADEOUT_DONE→IN_FADEIN→… と多段遷移しても
        // Update は最後の値しか観測できないが、fade-in は await DOFade(≥0.4s/通常 1s) を挟むので
        // IN_FADEIN は最低でも複数フレーム継続し、Update で必ず観測できる（取りこぼしなし）。
        private sealed class FadeStateLatch : IObserver<ScreenFade.FadeState>
        {
            public ScreenFade.FadeState State = ScreenFade.FadeState.NONE;
            public void OnNext(ScreenFade.FadeState value) => State = value;
            // 想定外の購読断（以後 OnNext が来ずラッチが固着）を後追いできるよう警告のみ残す。
            // 購読終端でしか呼ばれない＝毎フレ経路ではないので spam にならない。
            public void OnError(Exception error) => Plugin.Log.LogWarning($"[TransitionGuard] fade 状態購読が OnError で終了: {error}");
            public void OnCompleted() { }
        }

        private readonly FadeStateLatch _fadeLatch = new FadeStateLatch();
        private IDisposable _fadeSub;
        private ScreenFade _subscribedFade;
        // IN_FADEIN への立ち上がり edge 検出用（config gate を織り込んだ値）。
        private bool _prevFadeInGate;

        private void Awake()
        {
            Instance = this;
        }

        // GBSystem.m_fade が現れたら（または別インスタンスへ差し替わったら）状態変化を購読する。
        // 購読時に ReactiveProperty が現在値を即 push するので、途中参加でもラッチが整合する。
        private void EnsureFadeSubscription()
        {
            ScreenFade fade = GBSystem.Instance != null ? GBSystem.Instance.m_fade : null;
            if (fade == _subscribedFade) return;
            _fadeSub?.Dispose();
            _fadeSub = null;
            _subscribedFade = fade;
            if (fade != null)
                _fadeSub = fade.OnStateChanged().Subscribe(_fadeLatch);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _fadeSub?.Dispose();
            _fadeSub = null;
            _subscribedFade = null;
        }

        /// <summary>遷移開始の通知（Harmony Prefix から。常にゲームのメインスレッド上）。</summary>
        public static void NotifyTransitionStart(string reason)
        {
            var inst = Instance;
            if (inst == null) return;
            inst.Apply(inst._state.NotifyStart(Time.time), reason);
        }

        private void Update()
        {
            EnsureFadeSubscription();

            // GBSystem 未生成（タイトル前等）の間は入力 disabled 扱いにしない。
            bool inputDisabled = GBSystem.Instance != null && GBSystem.Instance.IsInputDisabled();

            // fade-in 開始（IN_FADEIN）への立ち上がり edge で rig 再 attach を前倒しする。
            // gate OFF なら常に false ＝ NotifyFadeInStarted を呼ばない＝従来動作（fade 完全終了で復帰）。
            // IN_FADEIN は全 ChangeScene* 経路で load/UnloadUnusedAssets 完了後にしか来ない＝再 attach 安全。
            // 仮に購読が IN_FADEIN の最中に確立し即 push で _prevFadeInGate=false から edge が立っても、
            // NotifyFadeInStarted は guarding && armed gate ＝遷移外なら no-op・遷移中ならそれが正しい復帰＝実害なし。
            bool fadeInGate = global::BG2VR.Configs.TransitionReattachOnFadeIn.Value
                              && _fadeLatch.State == ScreenFade.FadeState.IN_FADEIN;
            if (fadeInGate && !_prevFadeInGate)
                Apply(_state.NotifyFadeInStarted(), "fade-in 開始（新シーン読込完了）");
            _prevFadeInGate = fadeInGate;

            // 遅延秒数は毎フレーム config 読み（解決済み値を引数で渡す＝live 反映が自動成立。Subscribe 不要）
            Apply(_state.Tick(inputDisabled, Time.time, global::BG2VR.Configs.TransitionReattachDelaySec.Value), null);
        }

        private void Apply(GuardAction action, string reason)
        {
            switch (action)
            {
                case GuardAction.Begin:
                    Plugin.Log.LogInfo($"[TransitionGuard] 遷移開始 → VR teardown。reason={reason}");
                    VRModCore.BeginTransitionGuard();
                    break;
                case GuardAction.End:
                    Plugin.Log.LogInfo("[TransitionGuard] 遷移完了 → VR 再 attach を許可。");
                    VRModCore.EndTransitionGuard();
                    try { TransitionEnded?.Invoke(); }
                    catch (Exception ex) { Plugin.Log.LogError($"[TransitionGuard] TransitionEnded ハンドラ例外: {ex}"); }
                    break;
                case GuardAction.BeginCooldown:
                    // re-attach 遅延の開始（VRModCore は呼ばない。End は cooldown 経過後に出る）
                    float delaySecs = global::BG2VR.Configs.TransitionReattachDelaySec.Value;
                    Plugin.Log.LogInfo(
                        $"[TransitionGuard] 遷移完了 → re-attach を {delaySecs:0.0}s 遅延（ロードバースト回避）。");
                    break;
                case GuardAction.None:
                default:
                    break;
            }
        }
    }
}
