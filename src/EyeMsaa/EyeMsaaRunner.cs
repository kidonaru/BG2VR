using UnityEngine;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using BG2VR.Patches.Settings;

namespace BG2VR.EyeMsaa
{
    /// <summary>
    /// VR 中だけ URP の msaaSampleCount を VrEyeMsaa config 値へ設定し、VR 終了で元値へ復元する runner。
    /// URP では MSAA は RenderTexture.antiAliasing でなく UniversalRenderPipelineAsset.msaaSampleCount が
    /// lever（2026-06-13 実機 bridge 確証）。msaaSampleCount はパイプライン全体（desktop 含む）に効く
    /// グローバル設定のため FramePacing と同じく rising edge capture / falling edge restore で残置しない。
    /// reflection 自体は fork の UrpReflection.TryGetPipelineMsaa/SetPipelineMsaa に集約済み。
    /// steady 中の Apply（current != desired）は「VR 中 MSAA は BG2VR 単独所有」の常時収束＝
    /// dropdown live 変更も外部書き換えも config 値へ戻す（本ゲームは VR 中に msaaSampleCount を触らない
    /// ので通常は rising の 1 回適用で足りる）。
    /// </summary>
    internal sealed class EyeMsaaRunner : MonoBehaviour
    {
        private bool m_prevEffective;
        private int m_savedMsaa;

        private void Update()
        {
            if (!UrpReflection.TryGetPipelineMsaa(out int current)) return; // built-in pipeline では何もしない（本ゲームは URP）

            bool effective = VRModCore.IsVrActive && VRModCore.IsXrSessionRunning;
            int desired = MsaaDropdownPolicy.Sanitize(ConfigManager.VrEyeMsaa.Value);

            switch (EyeMsaaPolicy.Evaluate(m_prevEffective, effective, current, desired))
            {
                case EyeMsaaAction.CaptureAndApply:
                    m_savedMsaa = current;
                    UrpReflection.SetPipelineMsaa(desired);
                    Plugin.Log.LogInfo($"[EyeMsaa] VR 中の MSAA を適用 (URP msaaSampleCount {m_savedMsaa}→{desired})。");
                    break;
                case EyeMsaaAction.Apply:
                    UrpReflection.SetPipelineMsaa(desired); // dropdown live 変更 / 外部書き換えの打ち消し
                    break;
                case EyeMsaaAction.Restore:
                    UrpReflection.SetPipelineMsaa(m_savedMsaa);
                    Plugin.Log.LogInfo($"[EyeMsaa] VR 終了 → MSAA を復元 (URP msaaSampleCount={m_savedMsaa})。");
                    break;
            }
            m_prevEffective = effective;
        }

        // 兄弟 runner と対称の復元保証（VR active のまま破棄でも適用値を残置しない）。
        private void OnDestroy()
        {
            if (!m_prevEffective) return;
            UrpReflection.SetPipelineMsaa(m_savedMsaa);
        }
    }
}
