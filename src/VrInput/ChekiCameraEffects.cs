using System;
using System.Reflection;
using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// Cheki カメラに URP post-process（グレーディング+Bloom）をゲーム同値で反映する reflection ヘルパ。
    /// BG2VR は URP を直参照しないため ACD（UniversalAdditionalCameraData）は reflection で扱う（fork 同方針）。
    ///
    /// <para>AA は専用処理を持たない: URP の MSAA lever は RenderTexture.antiAliasing でなく pipeline
    /// msaaSampleCount（EyeMsaaRunner が VR 中 VrEyeMsaa へ駆動・2026-06-13 確証）。post を有効化すると URP が
    /// post 用の中間バッファ経路を確保し、そこへ pipeline MSAA が乗って targetTexture へ resolve される見込み
    /// ＝AA は post 有効化の副産物（per-RT 付与はしない）。</para>
    ///
    /// <para>URP 不在／型・property 未解決／参照カメラ無しは全 API no-op（移植性・安全フォールバック）。</para>
    /// </summary>
    internal static class ChekiCameraEffects
    {
        private static bool s_resolved;
        private static bool s_available;
        private static Type s_acdType;
        private static PropertyInfo s_renderPostProcessing; // bool
        private static PropertyInfo s_antialiasing;         // enum AntialiasingMode
        private static PropertyInfo s_antialiasingQuality;  // enum AntialiasingQuality
        private static PropertyInfo s_volumeLayerMask;      // LayerMask

        private static void EnsureResolved()
        {
            if (s_resolved) return;
            s_resolved = true;

            s_acdType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (s_acdType == null)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    s_acdType = asm.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                    if (s_acdType != null) break;
                }
            if (s_acdType == null)
            {
                Plugin.Log.LogInfo("[ChekiFX] URP ACD 型を解決できない（URP 非使用なら正常）。post 反映無効。");
                return;
            }
            s_renderPostProcessing = s_acdType.GetProperty("renderPostProcessing");
            s_antialiasing = s_acdType.GetProperty("antialiasing");
            s_antialiasingQuality = s_acdType.GetProperty("antialiasingQuality");
            s_volumeLayerMask = s_acdType.GetProperty("volumeLayerMask");
            if (s_renderPostProcessing == null)
            {
                Plugin.Log.LogWarning("[ChekiFX] ACD renderPostProcessing の解決に失敗。post 反映無効。");
                return;
            }
            s_available = true;
            Plugin.Log.LogInfo("[ChekiFX] Cheki カメラの post-process 反映を有効化。");
        }

        /// <summary>
        /// 参照ゲームカメラの ACD 設定（AA モード/品質/volumeLayerMask）を target へコピーし post を有効化する。
        /// target に ACD が無ければ AddComponent で明示確保（初回 Render 前に renderPostProcessing を立てる）。
        /// reference は本編ゲームカメラ前提（target=m_cam 自身を渡さない＝自己コピーは無意味。呼び出し側は
        /// CameraFinder.FindGameCamera() を渡す＝m_cam も eye も MainCamera タグ非設定で自己/eye グラブは起きない）。
        /// allowMSAA は OFF 時に戻さないが無害（Camera フラグ・カメラ専有・per-RT MSAA は付与しない方針）。
        /// </summary>
        internal static void ApplyPostProcess(Camera target, Camera reference)
        {
            EnsureResolved();
            if (!s_available || target == null || reference == null) return;
            var refAcd = reference.GetComponent(s_acdType);
            if (refAcd == null) return; // 参照に ACD が無い＝コピー元なし（no-op）
            var acd = target.GetComponent(s_acdType) ?? target.gameObject.AddComponent(s_acdType);
            if (acd == null) return;

            // AA モード/品質/volumeLayerMask をゲーム同値にコピー（enum/LayerMask は boxed のまま set）。
            if (s_antialiasing != null) s_antialiasing.SetValue(acd, s_antialiasing.GetValue(refAcd));
            if (s_antialiasingQuality != null) s_antialiasingQuality.SetValue(acd, s_antialiasingQuality.GetValue(refAcd));
            if (s_volumeLayerMask != null) s_volumeLayerMask.SetValue(acd, s_volumeLayerMask.GetValue(refAcd));
            s_renderPostProcessing.SetValue(acd, true);
            target.allowMSAA = true; // MSAA lever は pipeline msaaSampleCount 側（post で中間バッファに乗る）
        }

        /// <summary>target の post を無効化する（VrGamePostProcess OFF・冪等。ACD 無しは no-op）。</summary>
        internal static void DisablePostProcess(Camera target)
        {
            EnsureResolved();
            if (!s_available || target == null) return;
            var acd = target.GetComponent(s_acdType);
            if (acd == null) return; // ACD 未付与＝そもそも post 無効（no-op）
            s_renderPostProcessing.SetValue(acd, false);
        }
    }
}
