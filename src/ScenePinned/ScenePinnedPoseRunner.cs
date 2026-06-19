using GB;
using GB.Bar.MiniGame;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityVRMod.Core;
using BG2VR.TransitionGuard;
using BG2VR.VrFade;

namespace BG2VR.ScenePinned
{
    /// <summary>
    /// 保存済み env では rig を固定 pose（ワールド絶対 pos+yaw）へ入場時1回スナップし、カメラ追従を抑止する
    /// （spec §3-§4）。未保存 env / 鉄骨 / env 不在は何もせず現行動作（カメラ anchor＋追従）に委ねる。
    /// 固定 pose は rig/env 変化時のみ適用（毎フレ再アサートしない）＝適用後は grip で自由に移動・再保存できる。
    /// hotkey（キーボード）で現在 rig pose を保存／消去する。
    ///
    /// 実行順: 本 Runner は Plugin で CameraPositionFollowRunner より先に AddComponent する。
    /// follow が同フレームで抑止フラグを読む前に IsCurrentEnvPinned を確定させるため（さもないと
    /// pinned env で follow が translate→pinned は非変化フレームで再スナップしない→ドリフトする）。
    /// </summary>
    internal sealed class ScenePinnedPoseRunner : MonoBehaviour
    {
        private PinnedPoseStore m_store;
        private Transform m_boundRig;
        private string m_boundEnvKey;

        // 遷移後フェード保持の状態（spec / camera 収束チラつき隠し）。
        // 保持開始=遷移完了イベント、解除=pinned 適用+settle / 非 pinned 解決 / 安全タイムアウト。
        private const float MaxHoldSec = 1.0f; // 安全上限（pinned 適用が来ない/環境未解決のまま固着しないため）
        private bool m_holdActive;
        private float m_holdStartTime;
        private float m_holdAppliedTime;

        private void Awake()
        {
            m_store = new PinnedPoseStore(PinnedPoseStore.DefaultPath());
            m_store.Load();
        }

        private void OnEnable()
        {
            TransitionGuardRunner.TransitionEnded += OnTransitionEnded;
        }

        private void OnDisable()
        {
            TransitionGuardRunner.TransitionEnded -= OnTransitionEnded;
            // Runner 停止時に黒が固着しないよう保持を必ず解除する。
            m_holdActive = false;
            VrFadeRunner.HoldBlack = false;
        }

        // 遷移完了の瞬間に保持開始（このときゲーム fade はまだ黒＝明ける直前）。
        private void OnTransitionEnded() => StartHold();

        // フェード保持を開始する（feature/fade-hold OFF なら no-op）。タイマーは毎回リセット＝
        // 直近の rig 出現/遷移完了から settle/timeout を計る。
        private void StartHold()
        {
            if (!global::BG2VR.Configs.ScenePinnedPose.Value || !global::BG2VR.Configs.ScenePinnedFadeHold.Value) return;
            m_holdActive = true;
            m_holdStartTime = Time.time;
            m_holdAppliedTime = -1f; // 新規適用のみ settle 判定に使う（古い適用時刻を無効化）
        }

        private void Update()
        {
            // env 型名は従来どおり ?. で解決（fake-null 時の挙動を変えないため変数抽出のみ）。
            // 進行中ミニゲームがあれば種別名を併せて渡し、複合キー化する（spec §3.1）。
            string envName = GBSystem.Instance?.GetActiveEnvScene()?.GetType().Name;
            MiniGameBase mg = MiniGameBase.s_instance;
            string mgName = mg != null ? mg.GetMiniGameType().ToString() : null;
            string envKey = EnvKeyResolver.ResolveKey(envName, mgName);

            // master toggle OFF: pinned を無効化し全 env をカメラ追従へ戻す（保存データは保持）。
            if (!global::BG2VR.Configs.ScenePinnedPose.Value)
            {
                ScenePinnedPoseState.IsCurrentEnvPinned = false;
                m_boundRig = null;
                m_boundEnvKey = null;
                m_holdActive = false;
                VrFadeRunner.HoldBlack = false;
                return;
            }

            HandleHotkeys(envKey);

            Transform rig = VRModCore.GetRigTransform();
            // 初回ロード（TransitionGuard の遷移完了を経ない boot 経路）/ 遷移再 attach / VR 再有効化で
            // rig が出現した瞬間にも保持を開始する（rig 出現＝null→非null）。in-play の camera-flip 再 bind は
            // fork が同一 Update 内で破棄→再生成し null を経ないため、ここでは誤発火しない。
            if (m_boundRig == null && rig != null) StartHold();

            // pose は事前宣言（&& 短絡で TryGet 未呼出のとき default のまま＝definite assignment を満たす）。
            PinnedPose pose = default;
            bool isPinned = envKey != null && m_store.TryGet(envKey, out pose);

            // rig 差替え（遷移 teardown→再構築）または env 変化時のみ1回適用。
            if (isPinned && rig != null &&
                (!ReferenceEquals(rig, m_boundRig) || envKey != m_boundEnvKey))
            {
                rig.SetPositionAndRotation(pose.Position, Quaternion.Euler(0f, pose.Yaw, 0f));
                m_holdAppliedTime = Time.time;
                Plugin.Log.LogInfo($"[ScenePinnedPose] 固定位置を適用: env='{envKey}' pos={pose.Position} yaw={pose.Yaw:F1}");
            }

            m_boundRig = rig;
            m_boundEnvKey = envKey;
            ScenePinnedPoseState.IsCurrentEnvPinned = isPinned;

            UpdateFadeHold(envKey, isPinned);
        }

        // 遷移後フェード保持の解除判定 + VrFadeRunner への黒保持要求。
        private void UpdateFadeHold(string envKey, bool isPinned)
        {
            if (!m_holdActive)
            {
                VrFadeRunner.HoldBlack = false;
                return;
            }

            bool timeout = Time.time - m_holdStartTime > MaxHoldSec;
            // env が解決して非 pinned＝カメラ追従が正常動作する env。隠す必要なし→即解除（非 pinned 遷移を黒で待たせない）。
            bool resolvedNonPinned = envKey != null && !isPinned;
            // pinned 適用後 settle 経過＝カメラ収束のレイト再アンカーをカバーしてから明ける。
            // settle は apply 発火（=rig/env 変化）が前提。遷移は fork が rig を teardown→新インスタンス
            // 再生成するため必ず apply が発火する。万一 apply が再発火しない経路でも MaxHoldSec が backstop。
            bool pinnedSettled = isPinned && m_holdAppliedTime >= 0f &&
                                 Time.time - m_holdAppliedTime >= global::BG2VR.Configs.ScenePinnedFadeHoldSettleSec.Value;

            if (timeout || resolvedNonPinned || pinnedSettled)
            {
                m_holdActive = false;
                VrFadeRunner.HoldBlack = false;
                return;
            }
            // env 未解決の過渡（envKey==null）または pinned 適用待ちの間は黒を維持。
            VrFadeRunner.HoldBlack = true;
        }

        private void HandleHotkeys(string envKey)
        {
            if (global::BG2VR.Configs.SavePinnedPose.IsModifierKeyboardTriggered(global::BG2VR.Configs.PinnedPoseModifier.Value))
            {
                Transform rig = VRModCore.GetRigTransform();
                if (envKey == null || rig == null)
                {
                    Plugin.Log.LogInfo("[ScenePinnedPose] 保存対象外（鉄骨/環境なし/rig 不在）。保存をスキップ。");
                }
                else
                {
                    var pose = new PinnedPose(rig.position, rig.eulerAngles.y);
                    m_store.Set(envKey, pose);
                    m_boundEnvKey = null; // 次フレームで再適用判定を通す（保存値＝現在値なので見た目は不変）
                    Plugin.Log.LogInfo($"[ScenePinnedPose] 固定位置を保存: env='{envKey}' pos={pose.Position} yaw={pose.Yaw:F1}");
                    ShowToast("位置を保存しました");
                }
            }

            if (global::BG2VR.Configs.ClearPinnedPose.IsModifierKeyboardTriggered(global::BG2VR.Configs.PinnedPoseModifier.Value))
            {
                if (envKey == null)
                {
                    Plugin.Log.LogInfo("[ScenePinnedPose] 消去対象なし（鉄骨/環境なし）。");
                }
                else
                {
                    m_store.Remove(envKey);
                    Plugin.Log.LogInfo($"[ScenePinnedPose] 固定位置を消去: env='{envKey}'（次回入場からカメラ追従）。");
                    ShowToast("位置を消去しました");
                }
            }
        }

        // 保存/消去の成功時にゲームダイアログでトースト通知する（spec §3.2）。
        // master toggle OFF なら何もしない。fire-and-forget＝ホットキー処理をブロックしない。
        // CancellationToken は runner 破棄時にトースト待機をキャンセルする。
        private void ShowToast(string message)
        {
            if (!global::BG2VR.Configs.ShowPinnedPoseToast.Value) return;
            BG2VR.Utils.ConfirmDialogHelper
                .ShowInfoAsync(message, global::BG2VR.Configs.PinnedPoseToastSec.Value, this.GetCancellationTokenOnDestroy())
                .Forget();
        }
    }
}
