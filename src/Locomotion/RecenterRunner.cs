using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.Locomotion
{
    /// <summary>
    /// 正面リセット（recenter）の常駐 runner。起動時（初回のみ）と両手 Grip 長押しで
    /// VRModCore.RequestRecenter() を発火する（fork が OpenXR reference space を作り直す）。
    /// locomotion 設定から独立して走る＝grip 移動 OFF でも両グリップ recenter は働く。
    /// locomotion ON 時は GrabMoveState.ResetNow（移動量巻き戻し）と同じ 1.0s で同時発火＝統合挙動。
    /// spec: docs/superpowers/specs/2026-06-21-bg2vr-vr-recenter-design.md §5.2 §7
    /// </summary>
    internal sealed class RecenterRunner : MonoBehaviour
    {
        private readonly RecenterGesture m_gesture = new RecenterGesture();
        private bool m_prevVrActive;
        private bool m_didStartupRecenter; // session 内で 1 回のみ（起動時 recenter のラッチ）

        private void Update()
        {
            bool vrActive = VRModCore.IsVrActive;

            // 起動時（初回のみ）: VR 有効化の rising edge で 1 回。valid pose 待ちは fork 側 pending が吸収する。
            if (vrActive && !m_prevVrActive && !m_didStartupRecenter
                && global::BG2VR.Configs.RecenterOnStartup.Value)
            {
                VRModCore.RequestRecenter();
                m_didStartupRecenter = true;
            }
            m_prevVrActive = vrActive;

            if (!vrActive) { m_gesture.Clear(); return; }

            // 両手 Grip 長押し（locomotion 設定から独立・常時有効）。
            // grip snapshot は前回 Sync 時刻の値で 1 フレーム古いことがあるが、ここでは「発火トリガ」にのみ使う。
            // recenter の基準となる頭 pose は fork の TryApplyRecenter が当フレームで都度 locate する＝両者は意図的に独立。
            VrControllerSnapshot left = VRModCore.GetControllerSnapshot(VrHand.Left);
            VrControllerSnapshot right = VRModCore.GetControllerSnapshot(VrHand.Right);
            if (m_gesture.Update(left.Valid, left.Grip, right.Valid, right.Grip, Time.deltaTime))
                VRModCore.RequestRecenter();
        }
    }
}
