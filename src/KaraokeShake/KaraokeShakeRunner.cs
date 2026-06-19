using UnityEngine;
using UnityVRMod.Core;
using BG2VR.VrInput;
using BG2VR.UiSceneVoid;

namespace BG2VR.KaraokeShake
{
    /// <summary>
    /// カラオケ IN_GAME 中のみ、両手 snapshot の native 速度から振りを検知し ZL/ZR pulse を注入＋ハプティクス。
    /// ProjectorRunner.Update の定常状態でのみ Tick される（bridge 失効は Update 先頭の Clear が担う）。
    /// ゲート外（カラオケ外 / 非 IN_GAME / 無効 / VR 非 ready）では state をリセットして armed 残留を持ち越さない。
    /// 検知ロジックは純関数 KaraokeShakeDetector（テスト対象）。本 runner は速度供給と Config 解決のみ。
    /// </summary>
    internal sealed class KaraokeShakeRunner
    {
        private KaraokeShakeDetector.State m_left = KaraokeShakeDetector.NewState();
        private KaraokeShakeDetector.State m_right = KaraokeShakeDetector.NewState();
        private bool m_warnedNoVelocity; // velocity 未供給ランタイムの 1 回限り警告フラグ

        public void Tick(VrControllerSnapshot left, VrControllerSnapshot right, float dt)
        {
            if (!global::BG2VR.Configs.KaraokeShakeEnabled.Value || !MiniGameProbe.IsKaraokeInGame())
            {
                Reset();
                return;
            }

            // velocity 未供給ランタイム（VDXR 等）では振り入力が無反応になる＝原因が分かるよう 1 回だけ警告。
            if ((left.Valid && !left.VelocityValid) || (right.Valid && !right.VelocityValid))
            {
                if (!m_warnedNoVelocity)
                {
                    m_warnedNoVelocity = true;
                    Plugin.Log.LogWarning("[KaraokeShake] コントローラ速度が未供給(VelocityValid=false)＝振り入力は無効。ランタイムが XrSpaceVelocity 非対応の可能性。");
                }
            }

            var p = new KaraokeShakeDetector.Params
            {
                High = global::BG2VR.Configs.KaraokeShakeThreshold.Value,
                ReleaseRatio = global::BG2VR.Configs.KaraokeShakeReleaseRatio.Value,
                RefractorySec = global::BG2VR.Configs.KaraokeShakeRefractorySec.Value,
                DownWeight = global::BG2VR.Configs.KaraokeShakeDownWeight.Value,
                ForwardWeight = global::BG2VR.Configs.KaraokeShakeForwardWeight.Value,
                AngularWeight = global::BG2VR.Configs.KaraokeShakeAngularWeight.Value,
                LiftVetoSpeed = global::BG2VR.Configs.KaraokeShakeLiftVetoSpeed.Value,
                Smoothing = global::BG2VR.Configs.KaraokeShakeSmoothing.Value,
            };
            float amp = global::BG2VR.Configs.KaraokeHapticAmplitude.Value;
            float dur = global::BG2VR.Configs.KaraokeHapticDurationSec.Value;
            // pulse の getter Postfix と同じ入力ゲート＝pause/fade 中(IsInputDisabled)は pulse・ハプティクス両方を抑止する。
            // これが無いと「音符は入らないのに振動だけ鳴る」非整合が出る（pulse は Postfix 側で弾かれるため）。
            // 検知(Step)自体は常に進める＝この間の振りはスイングとして消費する（pulse 抑止と挙動を揃える）。
            bool allowed = GbGameButtonBridge.InputAllowed();

            // 左手 → タンバリン(ZL)
            if (left.Valid && left.VelocityValid)
            {
                if (KaraokeShakeDetector.Step(ref m_left, left.LinearVelocity, left.AngularVelocity.magnitude, dt, p) && allowed)
                {
                    GbGameButtonBridge.NotePulseZL = true;
                    VRModCore.TriggerHaptic(VrHand.Left, amp, dur);
                }
            }

            // 右手 → ガヤ(ZR)
            if (right.Valid && right.VelocityValid)
            {
                if (KaraokeShakeDetector.Step(ref m_right, right.LinearVelocity, right.AngularVelocity.magnitude, dt, p) && allowed)
                {
                    GbGameButtonBridge.NotePulseZR = true;
                    VRModCore.TriggerHaptic(VrHand.Right, amp, dur);
                }
            }
        }

        /// <summary>カラオケ離脱 / 遷移 / 無効化時に state を初期化（armed 残留防止）。</summary>
        public void Reset()
        {
            m_left = KaraokeShakeDetector.NewState();
            m_right = KaraokeShakeDetector.NewState();
        }
    }
}
