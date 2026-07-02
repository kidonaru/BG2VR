using System;
using System.Collections.Generic;
using BG2VR.VrInput;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// VR D3D12 NON_LOCAL leak 回避: MeshRenderer の URP/Lit material を per-renderer clone に差替える。
    /// 共有 Material instance の per-renderer native buffer 重複確保が leak 原因であり、
    /// clone で instance を分離するだけで leak が止まる（shader swap 不要）。
    /// </summary>
    internal static class VrLeakFixRunner
    {
        private const string OrigLitShaderName = "Universal Render Pipeline/Lit";
        private const int ScanIntervalFrames = 60;

        private const int TransparentThreshold = 3000;

        private struct OriginalState
        {
            public int InstanceID;
            public Material[] SharedMaterials;
            public Material[] ClonedMaterials;
            public int MaterialsLength;
            public int OrigSortingOrder;
            public bool HasTransparent;
            public bool DisabledForAdditive;
        }

        private static readonly Dictionary<Renderer, OriginalState> s_captured = new();
        private static bool s_configEnabled;
        private static int s_lastScanFrame = -1;
        private static bool s_hooksInstalled;
        private static AdditiveRedrawFeature s_additiveFeature;
        private static bool s_quittingHooked;
        private static bool s_pendingSceneSwap;
        private static bool s_firstCameraRenderDone;

        internal static int CapturedCount => s_captured.Count;

        private static bool IsOrigUrpLit(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.shader.name != OrigLitShaderName) return false;
            // 自前 clone（_leakfix suffix）を再 capture しない
            if (m.name.EndsWith("_leakfix")) return false;
            return true;
        }

        private static readonly int s_dstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_baseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_emissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_emissionMapId = Shader.PropertyToID("_EmissionMap");

        private static bool IsAdditiveBlend(Material m)
        {
            if (m == null || !m.HasProperty(s_dstBlendId)) return false;
            return (int)m.GetFloat(s_dstBlendId) == 1;
        }

        /// <summary>
        /// 毎フレーム呼ぶ。config ON 中は isVrSessionActive に関係なく swap を維持する。
        /// VR teardown 中に restore すると orig Lit が bind されて native buffer 確保 → leak 源になるため。
        /// </summary>
        internal static void Tick(bool isVrSessionActive)
        {
            try
            {
                bool cfgEnabled = ConfigManager.OpenXR_LeakFixEnabled?.Value ?? false;

                // config OFF → restore して停止
                if (!cfgEnabled)
                {
                    if (s_configEnabled)
                    {
                        RestoreAll("leak fix disabled");
                        s_captured.Clear();
                        UninstallHooks();
                        RemoveAdditiveFeature();
                        TransparentRedrawRunner.Uninstall();
                        SpotLightRayRedrawRunner.Uninstall();
                        s_configEnabled = false;
                    }
                    return;
                }

                // config ON（isVrSessionActive は見ない）
                if (!s_configEnabled)
                {
                    s_configEnabled = true;
                    InstallHooks();
                    EnsureQuittingHook();
                    InjectAdditiveFeature();
                    Plugin.Log.LogInfo("[VrLeakFixRunner] ON (config enabled)");
                }

                UrpFeatureInjector.EnsureInjected();

                // scene load / beginCameraRendering からの緊急 swap 要求
                if (s_pendingSceneSwap)
                {
                    s_pendingSceneSwap = false;
                    PruneDeadEntries();
                    int before = s_captured.Count;
                    CaptureAndSwap();
                    int after = s_captured.Count;
                    Plugin.Log.LogInfo($"[VrLeakFixRunner] scene swap: {before}→{after} captured");
                    DumpOrigLitSurvivors();
                    s_lastScanFrame = Time.frameCount;
                    return;
                }

                // 定期 scan
                int frameCount = Time.frameCount;
                if ((frameCount - s_lastScanFrame) >= ScanIntervalFrames)
                {
                    s_lastScanFrame = frameCount;
                    PruneDeadEntries();
                    CaptureAndSwap();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[VrLeakFixRunner] Tick 例外: {ex.Message}");
            }
        }

        // --- Hook install / uninstall ---

        private static void InstallHooks()
        {
            if (s_hooksInstalled) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            s_hooksInstalled = true;
            s_firstCameraRenderDone = false;
        }

        private static void UninstallHooks()
        {
            if (!s_hooksInstalled) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            s_hooksInstalled = false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            s_pendingSceneSwap = true;
            s_firstCameraRenderDone = false;
            Plugin.Log.LogInfo($"[VrLeakFixRunner] sceneLoaded: {scene.name} (mode={mode})");
        }

        private static void OnActiveSceneChanged(Scene prev, Scene next)
        {
            s_pendingSceneSwap = true;
            s_firstCameraRenderDone = false;
            Plugin.Log.LogInfo($"[VrLeakFixRunner] activeSceneChanged: {prev.name}→{next.name}");
        }

        /// <summary>
        /// 初回 camera render の直前に CaptureAndSwap を走らせる。
        /// scene load 後の最初の rendering で orig Lit が bind される前に swap を完了させる。
        /// </summary>
        private static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (s_firstCameraRenderDone) return;
            if (!s_configEnabled) return;

            s_firstCameraRenderDone = true;
            try
            {
                PruneDeadEntries();
                int before = s_captured.Count;
                CaptureAndSwap();
                int after = s_captured.Count;
                if (after != before)
                {
                    Plugin.Log.LogInfo($"[VrLeakFixRunner] beginCameraRendering swap: {before}→{after} captured (cam={cam.name})");
                    DumpOrigLitSurvivors();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[VrLeakFixRunner] beginCameraRendering 例外: {ex.Message}");
            }
        }

        // --- Capture & Swap ---

        private static readonly HashSet<int> s_registeredMeshIds = new();
        private static readonly HashSet<int> s_spotRegisteredMeshIds = new();

        private static void CaptureAndSwap()
        {
            Shader additiveShader = BundledShaders.AdditiveRedraw;

            var allMR = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            bool anyAdditiveRegistered = false;
            bool anySpotLightRegistered = false;

            for (int i = 0; i < allMR.Length; i++)
            {
                var r = allMR[i];
                if (r == null) continue;
                if (r.gameObject.scene.rootCount == 0) continue;

                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                if (s_captured.TryGetValue(r, out var existing))
                {
                    if (mats.Length != existing.MaterialsLength)
                    {
                        DestroyClones(existing.ClonedMaterials);
                        s_captured.Remove(r);
                    }
                    else continue;
                }

                bool anyLit = false;
                for (int j = 0; j < mats.Length; j++)
                {
                    if (IsOrigUrpLit(mats[j])) { anyLit = true; break; }
                }
                if (!anyLit) continue;

                var clones = new Material[mats.Length];
                var swapped = new Material[mats.Length];
                bool anyClone = false;
                bool needsSortingOrder = false;
                bool isAdditiveRenderer = false;
                int maxOrigRq = 0;

                for (int j = 0; j < mats.Length; j++)
                {
                    if (!IsOrigUrpLit(mats[j]))
                    {
                        swapped[j] = mats[j];
                        continue;
                    }
                    try
                    {
                        var clone = UnityEngine.Object.Instantiate(mats[j]);
                        clone.name = mats[j].name + "_leakfix";

                        int origRq = mats[j].renderQueue;
                        if (IsAdditiveBlend(mats[j]))
                        {
                            isAdditiveRenderer = true;
                        }
                        else if (origRq >= TransparentThreshold)
                        {
                            needsSortingOrder = true;
                            if (origRq > maxOrigRq) maxOrigRq = origRq;
                        }

                        clones[j] = clone;
                        swapped[j] = clone;
                        anyClone = true;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[VrLeakFixRunner] clone 失敗: {mats[j]?.name}: {ex.Message}");
                        swapped[j] = mats[j];
                    }
                }

                if (anyClone)
                {
                    s_captured[r] = new OriginalState
                    {
                        InstanceID = r.GetInstanceID(),
                        SharedMaterials = mats,
                        ClonedMaterials = clones,
                        MaterialsLength = mats.Length,
                        OrigSortingOrder = r.sortingOrder,
                        HasTransparent = needsSortingOrder,
                        // additive renderer は additiveShader の有無に関わらず必ず無効化するため、restore 判定も isAdditiveRenderer で持つ
                        DisabledForAdditive = isAdditiveRenderer,
                    };
                    try { r.sharedMaterials = swapped; }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[VrLeakFixRunner] swap 失敗: {r.gameObject?.name}: {ex.Message}");
                        DestroyClones(clones);
                        s_captured.Remove(r);
                        needsSortingOrder = false;
                        isAdditiveRenderer = false;
                    }

                    if (needsSortingOrder)
                    {
                        r.sortingOrder = maxOrigRq - 2000;
                    }

                    if (isAdditiveRenderer)
                    {
                        var goName = r.gameObject?.name;
                        bool isSpotLight = goName != null && goName.Contains("SpotLight");
                        // additive renderer は必ず scene draw から外す（orig-clone rQ=3000 の scene draw = leak trigger 回避）
                        r.enabled = false;
                        if (isSpotLight)
                        {
                            // SpotLightRay は skybox 領域に重なり ZWrite On の ScriptableRenderPass では
                            // 加算積算が壊れるため、endCameraRendering（skybox 後）で再描画する（Round 10K）。
                            // clone を使うため additiveShader（bundle）に依存しない
                            RegisterSpotLightRedraw(r, mats, clones);
                            anySpotLightRegistered = true;
                        }
                        else if (additiveShader != null)
                        {
                            RegisterAdditiveMeshDraw(r, mats, additiveShader);
                            anyAdditiveRegistered = true;
                        }
                        // additiveShader == null かつ非 SpotLight は redraw なし（従来どおり非表示・degraded だが leak-safe）
                    }
                }
            }

            if (anyAdditiveRegistered)
                TransparentRedrawRunner.Install();
            if (anySpotLightRegistered)
                SpotLightRayRedrawRunner.Install();
        }

        private static void RegisterAdditiveMeshDraw(MeshRenderer r, Material[] origMats, Shader additiveShader)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            var mesh = mf.sharedMesh;
            int meshId = mesh.GetInstanceID();

            Material origAdditiveMat = null;
            for (int j = 0; j < origMats.Length; j++)
            {
                if (IsOrigUrpLit(origMats[j]) && IsAdditiveBlend(origMats[j]))
                { origAdditiveMat = origMats[j]; break; }
            }
            if (origAdditiveMat == null) return;

            if (r.isPartOfStaticBatch)
            {
                if (s_registeredMeshIds.Contains(meshId)) return;
                s_registeredMeshIds.Add(meshId);

                var redraw = CreateAdditiveRedrawMaterial(origAdditiveMat, additiveShader);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    TransparentRedrawRunner.RegisterMeshDraw(
                        r, mesh, Matrix4x4.identity, redraw, origAdditiveMat.renderQueue, s);
                }
                Plugin.Log.LogInfo($"[VrLeakFixRunner] additive DrawMesh 登録 (static batch): mesh={mesh.name} submeshes={mesh.subMeshCount}");
            }
            else
            {
                var redraw = CreateAdditiveRedrawMaterial(origAdditiveMat, additiveShader);
                TransparentRedrawRunner.RegisterMeshDraw(
                    r, mesh, r.localToWorldMatrix, redraw, origAdditiveMat.renderQueue, 0);
                Plugin.Log.LogInfo($"[VrLeakFixRunner] additive DrawMesh 登録: {r.gameObject?.name}");
            }
        }

        /// <summary>
        /// SpotLightRay を endCameraRendering 再描画に登録する。
        /// orig URP/Lit additive material の per-renderer clone をそのまま使う（renderQueue 不変・leak-safe。Round 10K）。
        /// clone は s_captured[r].ClonedMaterials が所有するため SpotLightRayRedrawRunner 側では破棄しない。
        /// dedup は additive-redraw（skybox 前 ScriptableRenderPass）とは別経路なので専用 set を使う
        /// （s_registeredMeshIds を共有すると combined mesh 混載時に片方が mesh 丸ごと skip されうる）。
        /// </summary>
        private static void RegisterSpotLightRedraw(MeshRenderer r, Material[] origMats, Material[] cloneMats)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mesh = mf.sharedMesh;

            Material additiveClone = null;
            for (int j = 0; j < origMats.Length; j++)
            {
                if (IsOrigUrpLit(origMats[j]) && IsAdditiveBlend(origMats[j]))
                {
                    // clone があればそれを使う（Round 10K で leak-safe 検証済）。
                    // clone 生成失敗時のみ orig shared material に fallback（非 scene-draw なので理論上 leak-safe だが未検証）
                    additiveClone = cloneMats[j] != null ? cloneMats[j] : origMats[j];
                    break;
                }
            }
            if (additiveClone == null) return;

            if (r.isPartOfStaticBatch)
            {
                int meshId = mesh.GetInstanceID();
                if (s_spotRegisteredMeshIds.Contains(meshId)) return;
                s_spotRegisteredMeshIds.Add(meshId);
                for (int s = 0; s < mesh.subMeshCount; s++)
                    SpotLightRayRedrawRunner.Register(r, mesh, Matrix4x4.identity, additiveClone, s);
                Plugin.Log.LogInfo($"[VrLeakFixRunner] SpotLightRay redraw 登録 (static batch): mesh={mesh.name} submeshes={mesh.subMeshCount}");
            }
            else
            {
                SpotLightRayRedrawRunner.Register(r, mesh, r.localToWorldMatrix, additiveClone, 0);
                Plugin.Log.LogInfo($"[VrLeakFixRunner] SpotLightRay redraw 登録: {r.gameObject?.name}");
            }
        }

        private static Material CreateAdditiveRedrawMaterial(Material orig, Shader additiveShader)
        {
            var mat = new Material(additiveShader);
            mat.name = orig.name + "_additiveRedraw";
            if (orig.HasProperty(s_baseMapId))
                mat.SetTexture(s_baseMapId, orig.GetTexture(s_baseMapId));
            if (orig.HasProperty(s_baseColorId))
                mat.SetColor(s_baseColorId, orig.GetColor(s_baseColorId));
            if (orig.HasProperty(s_emissionColorId))
                mat.SetColor(s_emissionColorId, orig.GetColor(s_emissionColorId));
            if (orig.HasProperty(s_emissionMapId))
                mat.SetTexture(s_emissionMapId, orig.GetTexture(s_emissionMapId));
            return mat;
        }

        // --- Diagnostics ---

        /// <summary>
        /// swap 後に orig URP/Lit が残っている MR を検出してログ出力。
        /// leak 復帰時の原因特定用。
        /// </summary>
        private static void DumpOrigLitSurvivors()
        {
            int origCount = 0;
            var allMR = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            for (int i = 0; i < allMR.Length; i++)
            {
                var r = allMR[i];
                if (r == null || r.gameObject.scene.rootCount == 0) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int j = 0; j < mats.Length; j++)
                {
                    if (IsOrigUrpLit(mats[j]))
                    {
                        origCount++;
                        if (origCount <= 5)
                            Plugin.Log.LogWarning($"[VrLeakFixRunner] orig Lit 残存: {r.gameObject?.name} mat[{j}]={mats[j]?.name} shader={mats[j]?.shader?.name}");
                        break;
                    }
                }
            }
            if (origCount > 0)
                Plugin.Log.LogWarning($"[VrLeakFixRunner] orig Lit 残存 MR 計 {origCount} 件");
        }

        // --- Prune / Restore ---

        private static void PruneDeadEntries()
        {
            List<Renderer> dead = null;
            foreach (var kv in s_captured)
            {
                if (kv.Key != null && kv.Key.GetInstanceID() == kv.Value.InstanceID) continue;
                (dead ??= new List<Renderer>()).Add(kv.Key);
            }
            if (dead != null)
            {
                foreach (var k in dead)
                {
                    if (s_captured.TryGetValue(k, out var state))
                    {
                        DestroyClones(state.ClonedMaterials);
                        s_captured.Remove(k);
                    }
                }
            }
            TransparentRedrawRunner.PruneDeadEntries();
            SpotLightRayRedrawRunner.PruneDeadEntries();
        }

        private static void RestoreAll(string reason)
        {
            foreach (var kv in s_captured)
            {
                var r = kv.Key;
                var state = kv.Value;
                if (r != null && r.GetInstanceID() == state.InstanceID)
                {
                    if (state.DisabledForAdditive)
                    {
                        try { r.enabled = true; }
                        catch { }
                    }
                    if (state.SharedMaterials != null)
                    {
                        try { r.sharedMaterials = state.SharedMaterials; }
                        catch { }
                    }
                    if (state.HasTransparent)
                    {
                        try { r.sortingOrder = state.OrigSortingOrder; }
                        catch { }
                    }
                }
                DestroyClones(state.ClonedMaterials);
            }
            TransparentRedrawRunner.ClearEntries();
            SpotLightRayRedrawRunner.ClearEntries();
            s_registeredMeshIds.Clear();
            s_spotRegisteredMeshIds.Clear();
            Plugin.Log.LogInfo($"[VrLeakFixRunner] {reason}: restored={s_captured.Count}");
        }

        private static void DestroyClones(Material[] clones)
        {
            if (clones == null) return;
            for (int i = 0; i < clones.Length; i++)
            {
                if (clones[i] == null) continue;
                try
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(clones[i]);
                    else UnityEngine.Object.DestroyImmediate(clones[i]);
                }
                catch { }
                clones[i] = null;
            }
        }

        private static void EnsureQuittingHook()
        {
            if (s_quittingHooked) return;
            Application.quitting += () =>
            {
                try { RestoreAll("app quit"); } catch { }
                RemoveAdditiveFeature();
                SpotLightRayRedrawRunner.Uninstall();
                s_captured.Clear();
                UninstallHooks();
            };
            s_quittingHooked = true;
        }

        private static void InjectAdditiveFeature()
        {
            if (s_additiveFeature != null) return;
            s_additiveFeature = ScriptableObject.CreateInstance<AdditiveRedrawFeature>();
            s_additiveFeature.name = "BG2VR_AdditiveRedraw";
            if (UrpFeatureInjector.Register(s_additiveFeature))
            {
                Plugin.Log.LogInfo("[VrLeakFixRunner] AdditiveRedrawFeature 登録成功（ScriptableRenderPass + ZWrite On）");
            }
            else
            {
                Plugin.Log.LogWarning("[VrLeakFixRunner] AdditiveRedrawFeature 登録失敗");
                ScriptableObject.Destroy(s_additiveFeature);
                s_additiveFeature = null;
            }
        }

        private static void RemoveAdditiveFeature()
        {
            if (s_additiveFeature == null) return;
            UrpFeatureInjector.Unregister();
            ScriptableObject.Destroy(s_additiveFeature);
            s_additiveFeature = null;
        }
    }

    internal sealed class VrLeakFixRunnerBehaviour : MonoBehaviour
    {
        private void Update()
        {
            bool isVrSessionActive = VRModCore.IsVrActive && VRModCore.IsXrSessionRunning;
            VrLeakFixRunner.Tick(isVrSessionActive);
            NativeRenderPassDisableRunner.Tick();
        }
    }
}
