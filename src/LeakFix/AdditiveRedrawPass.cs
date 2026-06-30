using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// URP パイプライン内で加算ブレンド材質を DrawMesh する ScriptableRenderPass。
    /// AfterRenderingOpaques で実行。ZWrite On で depth を書き skybox pass の上書きを防ぐ。
    /// emission が極小のフラグメントは clip して skybox に穴を空けない。
    /// </summary>
    internal sealed class AdditiveRedrawPass : ScriptableRenderPass
    {
        internal struct MeshDrawEntry
        {
            public Renderer SourceRenderer;
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public Material RedrawMaterial;
            public int SubmeshIndex;
        }

        private static readonly List<MeshDrawEntry> s_entries = new();
        private static readonly HashSet<Material> s_ownedMaterials = new();
        private static AdditiveRedrawPass s_instance;

        internal static AdditiveRedrawPass Instance => s_instance ??= new AdditiveRedrawPass();
        internal static int EntryCount => s_entries.Count;
        internal static bool HasEntries => s_entries.Count > 0;

        private const string EyeCameraPrefix = "XrVrCamera";
        private const string PassName = "BG2VR_AdditiveRedraw";

        private AdditiveRedrawPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        internal static void RegisterMeshDraw(Renderer source, Mesh mesh, Matrix4x4 matrix,
            Material redrawMaterial, int submeshIndex)
        {
            s_entries.Add(new MeshDrawEntry
            {
                SourceRenderer = source,
                Mesh = mesh,
                Matrix = matrix,
                RedrawMaterial = redrawMaterial,
                SubmeshIndex = submeshIndex,
            });
            if (redrawMaterial != null)
                s_ownedMaterials.Add(redrawMaterial);
        }

        internal static void RemoveRenderer(Renderer renderer)
        {
            for (int i = s_entries.Count - 1; i >= 0; i--)
            {
                if (s_entries[i].SourceRenderer == renderer)
                    s_entries.RemoveAt(i);
            }
        }

        internal static void ClearEntries()
        {
            s_entries.Clear();
            foreach (var mat in s_ownedMaterials)
                DestroyMaterial(mat);
            s_ownedMaterials.Clear();
        }

        internal static void PruneDeadEntries()
        {
            for (int i = s_entries.Count - 1; i >= 0; i--)
            {
                if (s_entries[i].SourceRenderer == null)
                {
                    var mat = s_entries[i].RedrawMaterial;
                    if (mat != null && s_ownedMaterials.Remove(mat))
                        DestroyMaterial(mat);
                    s_entries.RemoveAt(i);
                }
            }
        }

        internal static IReadOnlyList<MeshDrawEntry> Entries => s_entries;

        // --- RenderGraph path (Unity 6000 URP default) ---

        private class PassData
        {
            public List<MeshDrawEntry> Entries;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (s_entries.Count == 0) return;

            var cameraData = frameData.Get<UniversalCameraData>();
            var cam = cameraData.camera;
            if (cam.targetTexture == null) return;
            if (!cam.name.StartsWith(EyeCameraPrefix)) return;
            if ((cam.cullingMask & 1) == 0) return;

            var resourceData = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddUnsafePass<PassData>(PassName, out var passData))
            {
                passData.Entries = s_entries;

                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Write);
                builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    for (int i = 0; i < data.Entries.Count; i++)
                    {
                        var e = data.Entries[i];
                        if (e.Mesh == null) continue;
                        if (e.SourceRenderer == null || !e.SourceRenderer.gameObject.activeInHierarchy) continue;
                        cmd.DrawMesh(e.Mesh, e.Matrix, e.RedrawMaterial, e.SubmeshIndex, 0);
                    }
                });
            }
        }

        // --- Legacy path ---

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }

        private static void DestroyMaterial(Material mat)
        {
            if (mat == null) return;
            try
            {
                if (Application.isPlaying) Object.Destroy(mat);
                else Object.DestroyImmediate(mat);
            }
            catch { }
        }
    }
}
