using UnityEngine;
using UnityVRMod.Features.Util;

namespace BG2VR.VrInput
{
    /// <summary>
    /// Cheki カメラに URP post-process（グレーディング+Bloom）をゲーム同値で反映するオーケストレータ。
    /// URP reflection 自体は fork の UrpReflection に集約済み（型/プロパティ解決はそちらが所有）。
    /// URP 不在／参照カメラに ACD 無し／参照カメラ無しは全 API no-op（移植性・安全フォールバック）。
    ///
    /// <para>AA は専用処理を持たない: URP の MSAA lever は RenderTexture.antiAliasing でなく pipeline
    /// msaaSampleCount（EyeMsaaRunner が VR 中 VrEyeMsaa へ駆動・2026-06-13 確証）。post を有効化すると URP が
    /// post 用の中間バッファ経路を確保し、そこへ pipeline MSAA が乗って targetTexture へ resolve される見込み
    /// ＝AA は post 有効化の副産物（per-RT 付与はしない）。</para>
    /// </summary>
    internal static class ChekiCameraEffects
    {
        /// <summary>
        /// 参照ゲームカメラの ACD 設定（AA モード/品質/volumeLayerMask）を target へコピーし post を有効化する。
        /// target に ACD が無ければ明示確保（初回 Render 前に renderPostProcessing を立てる）。
        /// reference は本編ゲームカメラ前提（target=m_cam 自身を渡さない＝自己コピーは無意味。呼び出し側は
        /// CameraFinder.FindGameCamera() を渡す＝m_cam も eye も MainCamera タグ非設定で自己/eye グラブは起きない）。
        /// 参照に ACD が無ければ丸ごと no-op（旧挙動の厳密温存。post だけ有効化しない）。
        /// </summary>
        internal static void ApplyPostProcess(Camera target, Camera reference)
        {
            if (!UrpReflection.AcdAvailable || target == null || reference == null) return;
            if (!UrpReflection.HasCameraData(reference)) return; // 参照に ACD 無し＝コピー元なし（丸ごと no-op）
            // 順序が不変条件: ACD 確保 → 設定コピー（renderPostProcessing は含まない）→ post 有効化。
            // SetRenderPostProcessing を先に呼ぶと初回 Render 前の post 確立タイミングが変わる。
            UrpReflection.EnsureCameraData(target.gameObject);
            UrpReflection.CopyPostProcessSettings(reference, target);
            UrpReflection.SetRenderPostProcessing(target, true);
            target.allowMSAA = true; // MSAA lever は pipeline msaaSampleCount 側（post で中間バッファに乗る）
        }

        /// <summary>target の post を無効化する（VrGamePostProcess OFF・冪等。ACD 無しは no-op）。</summary>
        internal static void DisablePostProcess(Camera target)
        {
            if (target == null) return;
            UrpReflection.SetRenderPostProcessing(target, false);
        }
    }
}
