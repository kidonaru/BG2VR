using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.FramePacing
{
    /// <summary>
    /// VR 中のフレームペーシング所有 runner。「IsVrActive かつ XR セッション running」の rising edge で
    /// 現在の targetFrameRate / vSyncCount を capture し、上限を撤廃（targetFrameRate=-1 / vSync=0）して
    /// HMD リフレッシュ同期に任せる。falling edge で capture 値へ復元する。
    /// セッションゲートの Why: rig ready は READY 待ち中も true で、その窓は xrWaitFrame スロットルが
    /// 無いため uncap だと Present 連打になる（タイトル画面 ~1760fps 実測・2026-06-11 検死）。
    /// NVIDIA driver の present キュー wedge の増悪要因なので、uncap は runtime がペーシングを
    /// 握っている間（session running）に限定する。reassert の打ち消しも同窓に限定される。
    /// capture は per-edge 再取得（遷移 teardown の往復ごとに取り直す＝VR off 中や遷移中に
    /// FixMod 側設定が変わっても常に直近の外部値へ復元される。計画の意味論決定）。
    /// UncapFrameRateReassert ON のときのみ毎フレーム乖離を監視して打ち消す（既定 OFF。
    /// ゲーム/FixMod の再設定は起動時+設定変更時のみなので通常は rising edge の 1 回適用で足りる）。
    /// </summary>
    internal sealed class FramePacingRunner : MonoBehaviour
    {
        private bool m_prevEffective;
        private int m_savedTargetFrameRate;
        private int m_savedVSyncCount;
        private bool m_reassertLogged;  // 打ち消しログは rising edge ごとに初回のみ（BepInEx ログ肥大防止）

        private void Update()
        {
            bool effective = VRModCore.IsVrActive && VRModCore.IsXrSessionRunning
                && global::BG2VR.Configs.UncapFrameRate.Value;
            bool reassert = global::BG2VR.Configs.UncapFrameRateReassert.Value;
            // edge フレームでは native getter の読み取りを行わない（コスト最小化）。steady-on 中は
            // reassert OFF でも乖離を観測する＝外部値の自己修復に使う（下記）
            bool diverged = effective && m_prevEffective
                && (Application.targetFrameRate != -1 || QualitySettings.vSyncCount != 0);
            if (diverged)
            {
                // 外部（ゲーム/FixMod）が上限を再設定した＝これが最新の外部意図なので復元先を更新する。
                // Why: 初回 capture はゲームの 60 設定より早く走り得て Unity 既定 -1 を保存してしまう
                // （2026-06-12 実機観測）。更新しないと以降の Restore が「上限なし」を書き戻し続け、
                // セッションゲートの遷移/standby 窓キャップが無力化する。打ち消すかは policy が判断。
                // 修復の発火は rising の次フレーム以降の steady-on（rising 同フレームの外部設定は
                // 次フレームで吸収＝m_saved が古いのは最大数フレームに限定）
                m_savedTargetFrameRate = Application.targetFrameRate;
                m_savedVSyncCount = QualitySettings.vSyncCount;
            }

            switch (FramePacingPolicy.Evaluate(m_prevEffective, effective, reassert, diverged))
            {
                case FramePacingAction.CaptureAndApply:
                    m_savedTargetFrameRate = Application.targetFrameRate;
                    m_savedVSyncCount = QualitySettings.vSyncCount;
                    m_reassertLogged = false;
                    Apply();
                    Plugin.Log.LogInfo(
                        $"[FramePacing] VR 中のフレームペーシングを適用 (targetFrameRate {m_savedTargetFrameRate}→-1, vSync {m_savedVSyncCount}→0)。");
                    break;

                case FramePacingAction.Restore:
                    Restore();
                    Plugin.Log.LogInfo(
                        $"[FramePacing] VR 終了 → フレームペーシングを復元 (targetFrameRate={m_savedTargetFrameRate}, vSync={m_savedVSyncCount})。");
                    break;

                case FramePacingAction.Reassert:
                    if (!m_reassertLogged)
                    {
                        m_reassertLogged = true;
                        Plugin.Log.LogInfo(
                            $"[FramePacing] 外部がフレームレート上限を再設定 (targetFrameRate={Application.targetFrameRate}, vSync={QualitySettings.vSyncCount}) → 打ち消して -1/0 を維持。");
                    }
                    Apply();
                    break;
            }

            m_prevEffective = effective;
        }

        // 退避値を持つ兄弟 runner（VrFade/HeadLook/Talk2DBackdrop）と対称の復元保証。
        // VR active のまま破棄される経路は現状ないが（BG2VR_Runtime は常駐）、将来の動的 teardown でも
        // 適用値を残置しない。
        private void OnDestroy()
        {
            if (!m_prevEffective) return;
            Restore();
        }

        /// <summary>
        /// 適用の単一正準経路（rising edge / Reassert の両方がここを通る）。
        /// targetFrameRate=-1（上限なし）+ vSync=0 で HMD リフレッシュ同期に任せる。
        /// </summary>
        private void Apply()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
        }

        private void Restore()
        {
            Application.targetFrameRate = m_savedTargetFrameRate;
            QualitySettings.vSyncCount = m_savedVSyncCount;
        }
    }
}
