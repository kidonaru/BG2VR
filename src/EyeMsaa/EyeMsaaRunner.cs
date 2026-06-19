using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityVRMod.Config;
using UnityVRMod.Core;
using BG2VR.Patches.Settings;

namespace BG2VR.EyeMsaa
{
    /// <summary>
    /// VR 中だけ URP の msaaSampleCount を VrEyeMsaa config 値へ設定し、VR 終了で元値へ復元する runner。
    /// URP では MSAA は RenderTexture.antiAliasing でなく UniversalRenderPipelineAsset.msaaSampleCount が
    /// lever（2026-06-13 実機 bridge 確証）。msaaSampleCount はパイプライン全体（desktop 含む）に効く
    /// グローバル設定のため FramePacing と同じく rising edge capture / falling edge restore で残置しない。
    /// msaaSampleCount は URP assembly 型なので reflection で get/set（参照アセンブリを増やさない）。
    /// steady 中の Apply（current != desired）は「VR 中 MSAA は BG2VR 単独所有」の常時収束＝
    /// dropdown live 変更も外部書き換えも config 値へ戻す（本ゲームは VR 中に msaaSampleCount を触らない
    /// ので通常は rising の 1 回適用で足りる）。
    /// </summary>
    internal sealed class EyeMsaaRunner : MonoBehaviour
    {
        private bool m_prevEffective;
        private int m_savedMsaa;
        private PropertyInfo m_prop;   // runner 1 個・型陳腐化リスク回避のためインスタンスフィールド

        private PropertyInfo Prop(object rp)
        {
            if (m_prop == null && rp != null)
                m_prop = rp.GetType().GetProperty("msaaSampleCount");
            return m_prop;
        }
        private int Get(object rp) { var p = Prop(rp); return p != null ? (int)p.GetValue(rp) : 1; }
        private void Set(object rp, int v) { if (rp == null) return; var p = Prop(rp); if (p != null) p.SetValue(rp, v); }

        private void Update()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return; // built-in pipeline では何もしない（本ゲームは URP）

            bool effective = VRModCore.IsVrActive && VRModCore.IsXrSessionRunning;
            int desired = MsaaDropdownPolicy.Sanitize(ConfigManager.VrEyeMsaa.Value);
            int current = Get(rp);

            switch (EyeMsaaPolicy.Evaluate(m_prevEffective, effective, current, desired))
            {
                case EyeMsaaAction.CaptureAndApply:
                    m_savedMsaa = current;
                    Set(rp, desired);
                    Plugin.Log.LogInfo($"[EyeMsaa] VR 中の MSAA を適用 (URP msaaSampleCount {m_savedMsaa}→{desired})。");
                    break;
                case EyeMsaaAction.Apply:
                    Set(rp, desired); // dropdown live 変更 / 外部書き換えの打ち消し
                    break;
                case EyeMsaaAction.Restore:
                    Set(rp, m_savedMsaa);
                    Plugin.Log.LogInfo($"[EyeMsaa] VR 終了 → MSAA を復元 (URP msaaSampleCount={m_savedMsaa})。");
                    break;
            }
            m_prevEffective = effective;
        }

        // 兄弟 runner と対称の復元保証（VR active のまま破棄でも適用値を残置しない）。
        private void OnDestroy()
        {
            if (!m_prevEffective) return;
            Set(GraphicsSettings.currentRenderPipeline, m_savedMsaa);
        }
    }
}
