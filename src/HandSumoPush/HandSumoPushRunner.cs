using UnityEngine;
using UnityVRMod.Core;
using BG2VR.VrInput;
using BG2VR.UiSceneVoid;

namespace BG2VR.HandSumoPush
{
    /// <summary>
    /// 手押し相撲 PLAYING 中のみ、両手 snapshot の native 速度から「両手を前に出す」を検知し
    /// ATriggered pulse を注入＋両手ハプティクス。ProjectorRunner.Update の定常状態でのみ Tick される
    /// （bridge pulse の失効は Update 先頭の Clear が担う）。gate 外（手押し相撲外 / 非 PLAYING / 無効 /
    /// VR 非 ready / 速度未供給）では state をリセットして armed 残留を持ち越さない。
    /// 検知ロジックは純関数 HandSumoPushDetector（テスト対象）。本 runner は速度供給と Config 解決のみ。
    /// </summary>
    internal sealed class HandSumoPushRunner
    {
        private HandSumoPushDetector.State m_state = HandSumoPushDetector.NewState();
        private bool m_warnedNoVelocity; // velocity 未供給ランタイムの 1 回限り警告フラグ

        public void Tick(VrControllerSnapshot left, VrControllerSnapshot right, float dt)
        {
            if (!global::BG2VR.Configs.HandSumoPushEnabled.Value || !MiniGameProbe.IsHandSumoPlaying())
            {
                Reset();
                return;
            }

            // velocity 未供給ランタイム（VDXR 等）では押し出し入力が無反応＝原因が分かるよう 1 回だけ警告。
            if ((left.Valid && !left.VelocityValid) || (right.Valid && !right.VelocityValid))
            {
                if (!m_warnedNoVelocity)
                {
                    m_warnedNoVelocity = true;
                    Plugin.Log.LogWarning("[HandSumoPush] コントローラ速度が未供給(VelocityValid=false)＝押し出し入力は無効。ランタイムが XrSpaceVelocity 非対応の可能性。");
                }
            }

            // 両手 AND が要件＝両手とも速度有効なときだけ検知（片手 invalid なら不発・state は保持）。
            if (!(left.Valid && left.VelocityValid && right.Valid && right.VelocityValid))
                return;

            var p = new HandSumoPushDetector.Params
            {
                High = global::BG2VR.Configs.HandSumoPushThreshold.Value,
                ReleaseRatio = global::BG2VR.Configs.HandSumoPushReleaseRatio.Value,
                RefractorySec = global::BG2VR.Configs.HandSumoPushRefractorySec.Value,
                CoincidenceSec = global::BG2VR.Configs.HandSumoPushCoincidenceSec.Value,
                Smoothing = global::BG2VR.Configs.HandSumoPushSmoothing.Value,
            };

            // pulse の getter Postfix と同じ入力ゲート＝pause/fade 中(IsInputDisabled)は pulse・ハプティクスを抑止。
            // 検知(Step)自体は常に進める（この間の押し出しもスイングとして消費＝pulse 抑止と挙動を揃える）。
            bool allowed = GbGameButtonBridge.InputAllowed();

            if (HandSumoPushDetector.Step(ref m_state, left.LinearVelocity, right.LinearVelocity, dt, p) && allowed)
            {
                GbGameButtonBridge.HandSumoPushPulse = true;
                float amp = global::BG2VR.Configs.HandSumoHapticAmplitude.Value;
                float dur = global::BG2VR.Configs.HandSumoHapticDurationSec.Value;
                VRModCore.TriggerHaptic(VrHand.Left, amp, dur);
                VRModCore.TriggerHaptic(VrHand.Right, amp, dur);
            }
        }

        /// <summary>手押し相撲離脱 / 遷移 / 無効化時に state を初期化（armed 残留防止）。</summary>
        public void Reset()
        {
            m_state = HandSumoPushDetector.NewState();
        }
    }
}
