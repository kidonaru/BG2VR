using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityVRMod.Config;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// URP の D3D12 native render pass を無効化する（RENDER_GRAPH_OLD_COMPILER define 相当を runtime で実現）。
    /// Unity 6000.0.58f1〜0.61f1 の D3D12 NON_LOCAL leak（エンジンバグ・6000.0.62f1 修正済）は
    /// leak 分岐が native render pass 使用時のみ成立するため、これを切ると leak が止まる
    /// （docs/vr-gpu-memory-leak-resolution.md / 調査レポート Round 10L・live 実証 2026-07-02）。
    ///
    /// 主経路: Harmony postfix で UniversalRenderer.supportsNativeRenderPassRendergraphCompiler を false 化。
    /// URP 自身が毎フレーム s_RenderGraph.nativeRenderPassesEnabled=false を書く形になるため、
    /// callback 順序・pipeline 再生成（s_RenderGraph 差し替え）に構造的に強い。
    /// patch 成功は「例外なし」ではなく「field が実際に false になった」の実測で判定し、
    /// 猶予内に false を観測できなければ beginCameraRendering + reflection の fallback へ自動降格する
    /// （silent 失敗 = leak が黙って続く事態を排除するため）。
    /// </summary>
    internal static class NativeRenderPassDisableRunner
    {
        private const string HarmonyId = "bg2vr.nrp-disable";

        /// <summary>
        /// patch 実効性検証の猶予。「field=true を観測した描画フレーム数」がこれを超えたら fallback へ降格。
        /// （rg 未生成で読めないフレームはカウントしない）
        /// </summary>
        private const int VerifyGraceFrames = 120;

        private static bool s_configEnabled;
        private static Harmony s_harmony;
        private static bool s_fallbackInstalled;
        private static bool s_verifyDone;
        private static int s_trueObservedFrames;

        private static FieldInfo s_rgField;   // UniversalRenderPipeline.s_RenderGraph（private static）
        private static PropertyInfo s_nrpProp; // RenderGraph.nativeRenderPassesEnabled
        private static bool s_reflectionResolved;

        /// <summary>毎フレーム呼ぶ（NativeRenderPassDisableRunnerBehaviour.Update）。config edge で install / uninstall する。</summary>
        internal static void Tick()
        {
            try
            {
                bool cfg = ConfigManager.OpenXR_NativeRenderPassDisable?.Value ?? false;

                if (!cfg)
                {
                    if (s_configEnabled) Uninstall("config OFF");
                    return;
                }

                if (!s_configEnabled)
                {
                    s_configEnabled = true;
                    Install();
                }

                VerifyPatchEffective();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NrpDisable] Tick 例外: {ex.Message}");
            }
        }

        private static void Install()
        {
            ResolveReflection();

            try
            {
                var getter = AccessTools.PropertyGetter(typeof(UniversalRenderer), "supportsNativeRenderPassRendergraphCompiler");
                if (getter == null) throw new MissingMemberException("supportsNativeRenderPassRendergraphCompiler getter が見つからない");

                s_harmony = new Harmony(HarmonyId);
                s_harmony.Patch(getter, postfix: new HarmonyMethod(typeof(NativeRenderPassDisableRunner), nameof(SupportsNrpPostfix)));
                Plugin.Log.LogInfo("[NrpDisable] ON: UniversalRenderer.supportsNativeRenderPassRendergraphCompiler を postfix（実効確認は field 観測で行う）");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NrpDisable] Harmony patch 失敗: {ex.Message} → fallback へ");
                s_harmony = null;
                InstallFallback();
            }

            s_verifyDone = false;
            s_trueObservedFrames = 0;
        }

        /// <summary>
        /// patch 実効性の自己検証。s_RenderGraph.nativeRenderPassesEnabled が false になったのを
        /// 一度観測できたら成功（以後サンプル停止）。true のまま猶予を超えたら fallback へ降格する。
        /// </summary>
        private static void VerifyPatchEffective()
        {
            if (s_verifyDone || s_fallbackInstalled) return;

            if (!s_reflectionResolved)
            {
                // 検証手段がない環境: patch 適用済みならそれを信じるしかない（fallback も同じ reflection に依存するため不可）
                s_verifyDone = true;
                Plugin.Log.LogWarning("[NrpDisable] reflection 解決失敗のため実効検証・fallback とも不可。Harmony patch のみ適用");
                return;
            }

            object rg = s_rgField.GetValue(null);
            if (rg == null) return; // pipeline 未生成のフレームは猶予にカウントしない

            bool value = (bool)s_nrpProp.GetValue(rg);
            if (!value)
            {
                s_verifyDone = true;
                Plugin.Log.LogInfo("[NrpDisable] 実効確認: nativeRenderPassesEnabled=false を観測");
                return;
            }

            s_trueObservedFrames++;
            if (s_trueObservedFrames > VerifyGraceFrames)
            {
                Plugin.Log.LogWarning($"[NrpDisable] 猶予 {VerifyGraceFrames} frame 内に false を観測できず（patch 無効の疑い）→ fallback へ降格");
                InstallFallback();
            }
        }

        /// <summary>
        /// fallback: 毎カメラ描画直前に s_RenderGraph.nativeRenderPassesEnabled=false を直書きする
        /// （live 実証済みの方式。s_RenderGraph は pipeline 再生成で差し替わるため毎回 GetValue する）。
        /// </summary>
        private static void InstallFallback()
        {
            if (s_fallbackInstalled) return;
            if (!s_reflectionResolved)
            {
                Plugin.Log.LogWarning("[NrpDisable] fallback も不可（reflection 解決失敗）");
                return;
            }
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            s_fallbackInstalled = true;
            Plugin.Log.LogInfo("[NrpDisable] fallback ON: beginCameraRendering で nativeRenderPassesEnabled=false を直書き");
        }

        private static void Uninstall(string reason)
        {
            try { s_harmony?.UnpatchSelf(); } catch { }
            s_harmony = null;
            if (s_fallbackInstalled)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                s_fallbackInstalled = false;
            }
            s_configEnabled = false;
            s_verifyDone = false;
            s_trueObservedFrames = 0;
            // native RP は次フレームから URP が自然に復元する（nativeRenderPassesEnabled は URP が毎フレーム書き直す）
            Plugin.Log.LogInfo($"[NrpDisable] OFF ({reason})");
        }

        private static void ResolveReflection()
        {
            if (s_reflectionResolved) return;
            try
            {
                s_rgField = typeof(UniversalRenderPipeline).GetField("s_RenderGraph", BindingFlags.NonPublic | BindingFlags.Static);
                if (s_rgField == null) return;
                s_nrpProp = s_rgField.FieldType.GetProperty("nativeRenderPassesEnabled",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                s_reflectionResolved = s_nrpProp != null && s_nrpProp.CanWrite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NrpDisable] reflection 解決例外: {ex.Message}");
            }
        }

        private static void SupportsNrpPostfix(ref bool __result) => __result = false;

        private static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            // 描画 callback 内は best-effort（例外で描画を壊さない）
            try
            {
                object rg = s_rgField.GetValue(null);
                if (rg != null) s_nrpProp.SetValue(rg, false);
            }
            catch { }
        }
    }

    /// <summary>NativeRenderPassDisableRunner を毎フレーム駆動する MonoBehaviour（Plugin が 1 個生成）。</summary>
    internal sealed class NativeRenderPassDisableRunnerBehaviour : MonoBehaviour
    {
        private void Update()
        {
            NativeRenderPassDisableRunner.Tick();
        }
    }
}
