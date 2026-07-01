using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// SpotLightRay（重なり合う加算透過光線・skybox 領域に重なる）を
    /// RenderPipelineManager.endCameraRendering（skybox pass の後）で CommandBuffer.DrawMesh により再描画する。
    ///
    /// ScriptableRenderPass (AfterRenderingOpaques) 経路は skybox 前でしか描けず、skybox 上書き対策の
    /// ZWrite On が光線同士の加算積算を壊すため SpotLightRay には使えない（Round 10J）。
    /// endCameraRendering は skybox 後に発火するため ZWrite 不要で加算積算・skybox 合成が正しくなる。
    /// immediate な Graphics.DrawMesh / RenderMesh は fork の手動 eye render（enabled=false のカメラ）に
    /// 届かないため使えない（Round 10K で live 実証。eye 到達・skybox 合成・leak-safe も同 Round で確認済み）。
    /// </summary>
    internal static class SpotLightRayRedrawRunner
    {
        internal struct DrawEntry
        {
            public Renderer Source;    // 登録元（prune 判定用）
            public Mesh Mesh;
            public Matrix4x4 Matrix;   // static batch は identity（combined mesh が world 座標）
            public Material Material;  // orig URP/Lit additive の per-renderer clone（s_captured 所有・ここでは破棄しない）
            public int SubmeshIndex;
        }

        private static readonly List<DrawEntry> s_entries = new();
        private static CommandBuffer s_cmd;
        private static bool s_installed;

        internal static int EntryCount => s_entries.Count;

        internal static void Install()
        {
            if (s_installed) return;
            s_cmd = new CommandBuffer { name = "BG2VR SpotLightRay Redraw" };
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            s_installed = true;
            Plugin.Log.LogInfo("[SpotLightRayRedrawRunner] installed");
        }

        internal static void Uninstall()
        {
            if (!s_installed) return;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (s_cmd != null) { s_cmd.Release(); s_cmd = null; }
            s_installed = false;
            ClearEntries();
            Plugin.Log.LogInfo("[SpotLightRayRedrawRunner] uninstalled");
        }

        internal static void Register(Renderer source, Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex)
        {
            s_entries.Add(new DrawEntry
            {
                Source = source,
                Mesh = mesh,
                Matrix = matrix,
                Material = material,
                SubmeshIndex = submeshIndex,
            });
        }

        internal static void ClearEntries() => s_entries.Clear();

        internal static void PruneDeadEntries()
        {
            for (int i = s_entries.Count - 1; i >= 0; i--)
            {
                var e = s_entries[i];
                // Source/Mesh/Material のいずれかが destroy 済み（Unity fake-null）なら除去
                if (e.Source == null || e.Mesh == null || e.Material == null)
                    s_entries.RemoveAt(i);
            }
        }

        // eye camera（fork が手動 render する XrVrCamera_Left/Right・L/R の 2 個で永続）を参照でキャッシュし、
        // 毎フレーム発火するホットパスでの cam.name string allocation を避ける。scene reload で eye camera が
        // 破棄されるとスロットは Unity fake-null で「空き」判定になり、新 eye camera で自動的に上書きされる（self-heal）。
        private static Camera s_eyeCamA;
        private static Camera s_eyeCamB;

        private static bool IsEyeCamera(Camera cam)
        {
            // 参照一致の fast-path（string alloc 無し）。cached camera が destroy 済みでも ReferenceEquals は不一致になり name 判定へ落ちる
            if (ReferenceEquals(cam, s_eyeCamA) || ReferenceEquals(cam, s_eyeCamB)) return true;
            var camName = cam.name; // fast-path 外でのみ alloc。Ordinal で culture 非依存比較
            if (camName == null || !camName.StartsWith("XrVrCamera", StringComparison.Ordinal)) return false;
            // 検証済みの eye camera を空きスロットへキャッシュ（fake-null スロットも空き扱い）
            if (s_eyeCamA == null || ReferenceEquals(s_eyeCamA, cam)) s_eyeCamA = cam;
            else s_eyeCamB = cam;
            return true;
        }

        /// <summary>
        /// eye camera（fork が手動 render する XrVrCamera_Left/Right）の render 完了直後に発火。
        /// skybox pass の後なので、加算光芒が skybox 領域でも消えずに正しく合成される。
        /// </summary>
        private static void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (s_entries.Count == 0 || s_cmd == null || cam == null) return;

            // eye camera のみ対象。UI / game / capture カメラは除外
            if (!IsEyeCamera(cam)) return;

            // eye RT が bind されている前提の描画（下記 ExecuteCommandBuffer は明示 SetRenderTarget を
            // 行わず「render 完了時に暗黙 bind 済みの eye RT + 行列」に依存する。Round 10K で実証。
            // Round 10J で明示 SetRenderTarget は描画崩壊が確認されているため明示しない）。
            // targetTexture が無い状態（万一 eye cam が screen render した等）は backbuffer 汚染回避で skip
            if (cam.targetTexture == null) return;

            // 2D UI / void 状態（default layer が cullingMask から外れている）はスキップ
            // = 既存 additive redraw と同じ可視性ポリシー（EyeCullingCoordinator の void 状態 = layer 29+30 のみ）
            if ((cam.cullingMask & 1) == 0) return;

            // sibling の OnBeginCameraRendering / Tick と同じく best-effort。stale SubmeshIndex 等で DrawMesh が
            // 例外を投げても毎フレーム spam させず、その frame の描画だけ諦める（ctx.Submit 未到達も許容）。
            try
            {
                s_cmd.Clear();
                for (int i = 0; i < s_entries.Count; i++)
                {
                    var e = s_entries[i];
                    if (e.Mesh == null || e.Material == null) continue;
                    // pass 0 = URP/Lit ForwardLit。emission-only 材質なので lighting context 不在でも正しく描ける。
                    // material の ZWrite Off + Blend One One + ZTest LEqual で加算積算 + キャラ遮蔽。
                    s_cmd.DrawMesh(e.Mesh, e.Matrix, e.Material, e.SubmeshIndex, 0);
                }
                ctx.ExecuteCommandBuffer(s_cmd);
                ctx.Submit();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpotLightRayRedrawRunner] OnEndCameraRendering 例外: {ex.Message}");
            }
        }
    }
}
