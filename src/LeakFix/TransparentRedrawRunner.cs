using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityVRMod.Core;

namespace BG2VR.LeakFix
{
    internal static class TransparentRedrawRunner
    {
        private static readonly int s_shArId = Shader.PropertyToID("unity_SHAr");
        private static readonly int s_shAgId = Shader.PropertyToID("unity_SHAg");
        private static readonly int s_shAbId = Shader.PropertyToID("unity_SHAb");
        private static readonly int s_shBrId = Shader.PropertyToID("unity_SHBr");
        private static readonly int s_shBgId = Shader.PropertyToID("unity_SHBg");
        private static readonly int s_shBbId = Shader.PropertyToID("unity_SHBb");
        private static readonly int s_shCId  = Shader.PropertyToID("unity_SHC");

        internal struct TransparentEntry
        {
            public Renderer Renderer;
            public Material RedrawMaterial;
            public int OrigRenderQueue;
            public int SubmeshIndex;
        }

        private static readonly List<TransparentEntry> s_entries = new();
        private static CommandBuffer s_cmd;
        private static bool s_installed;

        internal static int EntryCount => s_entries.Count + AdditiveRedrawPass.EntryCount;

        internal static void Install()
        {
            if (s_installed) return;
            VRModCore.SetSceneTransparentRedraw(OnTransparentRedraw);
            s_installed = true;
            Plugin.Log.LogInfo("[TransparentRedrawRunner] installed");
        }

        internal static void Uninstall()
        {
            if (!s_installed) return;
            VRModCore.SetSceneTransparentRedraw(null);
            s_installed = false;
            ClearEntries();
            Plugin.Log.LogInfo("[TransparentRedrawRunner] uninstalled");
        }

        internal static void Register(Renderer renderer, Material redrawMaterial, int origRenderQueue, int submeshIndex)
        {
            s_entries.Add(new TransparentEntry
            {
                Renderer = renderer,
                RedrawMaterial = redrawMaterial,
                OrigRenderQueue = origRenderQueue,
                SubmeshIndex = submeshIndex,
            });
        }

        /// <summary>
        /// 加算ブレンド材質の DrawMesh 登録。AdditiveRedrawPass（ScriptableRenderPass）に委譲。
        /// </summary>
        internal static void RegisterMeshDraw(Renderer source, Mesh mesh, Matrix4x4 matrix,
            Material redrawMaterial, int origRenderQueue, int submeshIndex)
        {
            AdditiveRedrawPass.RegisterMeshDraw(source, mesh, matrix, redrawMaterial, submeshIndex);
        }

        internal static void RemoveRenderer(Renderer renderer)
        {
            for (int i = s_entries.Count - 1; i >= 0; i--)
            {
                if (s_entries[i].Renderer == renderer)
                {
                    DestroyMaterial(s_entries[i].RedrawMaterial);
                    s_entries.RemoveAt(i);
                }
            }
            AdditiveRedrawPass.RemoveRenderer(renderer);
        }

        internal static void ClearEntries()
        {
            for (int i = 0; i < s_entries.Count; i++)
                DestroyMaterial(s_entries[i].RedrawMaterial);
            s_entries.Clear();
            AdditiveRedrawPass.ClearEntries();
        }

        internal static void PruneDeadEntries()
        {
            for (int i = s_entries.Count - 1; i >= 0; i--)
            {
                if (s_entries[i].Renderer == null)
                {
                    DestroyMaterial(s_entries[i].RedrawMaterial);
                    s_entries.RemoveAt(i);
                }
            }
            AdditiveRedrawPass.PruneDeadEntries();
        }

        /// <summary>
        /// fork callback（Camera.Render() 直後）。TransparentEntry (DrawRenderer) を実行。
        /// </summary>
        private static void OnTransparentRedraw(Camera eyeCam, RenderTexture target)
        {
            if (s_entries.Count == 0 || target == null || eyeCam == null) return;

            OverlayDrawOrder.StableSortByKey(s_entries, e => e.OrigRenderQueue);

            s_cmd ??= new CommandBuffer { name = "BG2VR_TransparentRedraw" };
            s_cmd.Clear();
            s_cmd.SetRenderTarget(target);
            s_cmd.SetViewProjectionMatrices(
                eyeCam.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(eyeCam.projectionMatrix, false));

            PushSHToCommandBuffer(s_cmd);

            for (int i = 0; i < s_entries.Count; i++)
            {
                var e = s_entries[i];
                if (e.Renderer == null || !e.Renderer.gameObject.activeInHierarchy) continue;
                s_cmd.DrawRenderer(e.Renderer, e.RedrawMaterial, e.SubmeshIndex, 0);
            }
            Graphics.ExecuteCommandBuffer(s_cmd);
        }

        private static void PushSHToCommandBuffer(CommandBuffer cmd)
        {
            var probe = RenderSettings.ambientProbe;
            cmd.SetGlobalVector(s_shArId, new Vector4(probe[0, 1], probe[0, 2], probe[0, 3], probe[0, 0]));
            cmd.SetGlobalVector(s_shAgId, new Vector4(probe[1, 1], probe[1, 2], probe[1, 3], probe[1, 0]));
            cmd.SetGlobalVector(s_shAbId, new Vector4(probe[2, 1], probe[2, 2], probe[2, 3], probe[2, 0]));
            cmd.SetGlobalVector(s_shBrId, new Vector4(probe[0, 4], probe[0, 5], probe[0, 6], probe[0, 7]));
            cmd.SetGlobalVector(s_shBgId, new Vector4(probe[1, 4], probe[1, 5], probe[1, 6], probe[1, 7]));
            cmd.SetGlobalVector(s_shBbId, new Vector4(probe[2, 4], probe[2, 5], probe[2, 6], probe[2, 7]));
            cmd.SetGlobalVector(s_shCId,  new Vector4(probe[0, 8], probe[1, 8], probe[2, 8], 0));
        }

        private static void DestroyMaterial(Material mat)
        {
            if (mat == null) return;
            try
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mat);
                else UnityEngine.Object.DestroyImmediate(mat);
            }
            catch { }
        }
    }
}
