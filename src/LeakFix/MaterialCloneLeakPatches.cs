using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using GB.Bar.MiniGame;
using GB.Scene;
using GB.Game.Params;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// ゲーム本体の Renderer.material / .materials 経由の Material clone leak を修正する Harmony パッチ群。
    /// v1.0.5 で一部修正済みだが直し残しがあるメソッドを対象に、.sharedMaterial + MaterialPropertyBlock
    /// または clone 追跡 + Object.Destroy で native perMaterialCB (D3D12 UPLOAD heap) の leak を防ぐ。
    /// </summary>
    [HarmonyPatch]
    internal static class MaterialCloneLeakPatches
    {
        private static readonly List<Material> s_karaokeStencilClones = new();
        private static readonly MaterialPropertyBlock s_mpb = new();

        internal static void Install()
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        // ════════════════════════════════════════════════════════════════
        //  シーンアンロード時の orphan clone 回収（async メソッド内の clone 等を安全網で回収）
        // ════════════════════════════════════════════════════════════════

        private static void OnSceneUnloaded(Scene scene)
        {
            var rendererMats = new HashSet<int>();
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                    if (m != null) rendererMats.Add(m.GetInstanceID());
            }

            int destroyed = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null) continue;
                if (m.GetInstanceID() >= 0) continue;
                if (m.hideFlags != HideFlags.None) continue;
                if (!m.name.EndsWith(" (Instance)")) continue;
                if (rendererMats.Contains(m.GetInstanceID())) continue;
                Object.Destroy(m);
                destroyed++;
            }

            if (destroyed > 0)
                Plugin.Log.LogInfo($"[MaterialCloneLeakFix] シーンアンロード時に orphan material clone {destroyed} 個を破棄");
        }

        // ════════════════════════════════════════════════════════════════
        //  CharacterHandle.ChangeDrinkColor — .material = x + .material.SetColor → sharedMaterial + MPB
        // ════════════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(CharacterHandle), nameof(CharacterHandle.ChangeDrinkColor))]
        [HarmonyPrefix]
        private static bool ChangeDrinkColor_Prefix(CharacterHandle __instance, DrinkParam param,
            ref GameObject ___m_chara)
        {
            if (___m_chara == null) return false;
            var smrs = ___m_chara.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var x in smrs)
            {
                if (x.name != "Glass06_rig:Liquid7" && x.name != "Liquid2") continue;
                x.sharedMaterial = param.Material;
                s_mpb.Clear();
                s_mpb.SetColor("_BaseColor", param.BaseMapColor);
                s_mpb.SetColor("_EmissionColor", param.EmissionMapColor);
                x.SetPropertyBlock(s_mpb);
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  CharacterHandle.ChangeSauceBottleColor — .material.SetColor → MPB
        // ════════════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(CharacterHandle), nameof(CharacterHandle.ChangeSauceBottleColor))]
        [HarmonyPrefix]
        private static bool ChangeSauceBottleColor_Prefix(ref List<SkinnedMeshRenderer> ___m_props, Color32 color)
        {
            if (___m_props == null || ___m_props.Count <= 10) return false;
            var smr = ___m_props[10];
            if (smr == null) return false;
            s_mpb.Clear();
            s_mpb.SetColor("_BaseColor", (Color)color);
            smr.SetPropertyBlock(s_mpb);
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  HidePantiesSphere.applyIntensity — .material.SetFloat → MPB
        // ════════════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(HidePantiesSphere), "applyIntensity")]
        [HarmonyPrefix]
        private static bool ApplyIntensity_Prefix(HidePantiesSphere __instance,
            ref MeshRenderer ___m_renderer, ref float ___m_intensity)
        {
            if (___m_renderer == null) return false;
            s_mpb.Clear();
            s_mpb.SetFloat("_Intensity", 10f * ___m_intensity);
            ___m_renderer.SetPropertyBlock(s_mpb);
            __instance.gameObject.SetActive(___m_intensity > 0f);
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  Psyllium.Setup — .materials get/set → .sharedMaterials get/set
        // ════════════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(Psyllium), nameof(Psyllium.Setup))]
        [HarmonyPrefix]
        private static bool PsylliumSetup_Prefix(GB.Game.CharID charID,
            ref RenderTexture __result,
            ref SkinnedMeshRenderer ___m_skinnedMeshRenderer,
            ref List<Material> ___m_materials,
            ref Camera ___m_renderCamera,
            ref RenderTexture ___m_renderTexture)
        {
            var mats = ___m_skinnedMeshRenderer.sharedMaterials;
            mats[0] = ___m_materials[(int)charID];
            ___m_skinnedMeshRenderer.sharedMaterials = mats;
            ___m_renderTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            ___m_renderCamera.targetTexture = ___m_renderTexture;
            __result = ___m_renderTexture;
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  Karaoke.setupHidePantiesSphere — stencil は MPB 不可 → clone 追跡 + Destroy
        //  clone を s_karaokeStencilClones に蓄積し、Karaoke 終了時 + シーンアンロード時に破棄。
        // ════════════════════════════════════════════════════════════════

        // sphere インスタンス化（m_hidePantiesSphere 生成 + findHipJoint）は省略:
        // 元のゲームコードで m_hidePantiesEnabled が最後に必ず false に上書きされるため sphere は機能的に未使用。
        // sphere 生成には private KaraokeUI m_ui + private findHipJoint への reflection が必要で、
        // 未使用機能のために reflection 依存を増やすのはリスク対効果が悪い。
        [HarmonyPatch(typeof(Karaoke), "setupHidePantiesSphere")]
        [HarmonyPrefix]
        private static bool SetupHidePantiesSphere_Prefix(GameObject chara,
            ref bool ___m_hidePantiesEnabled)
        {
            DestroyKaraokeStencilClones();

            var smrs = chara.GetComponentsInChildren<SkinnedMeshRenderer>();
            ___m_hidePantiesEnabled = false;

            var skinNames = new[] { "m_skin", "m_panties", "m_stockings", "m_fishnetstockings" };
            foreach (var smr in smrs)
            {
                var origMats = smr.sharedMaterials;
                var cloned = new Material[origMats.Length];
                for (int i = 0; i < origMats.Length; i++)
                {
                    var clone = new Material(origMats[i]);
                    clone.name = origMats[i].name + " (Instance)";
                    clone.SetFloat("_StencilComp", 8f);
                    clone.SetInt("_StencilMode", 2);
                    clone.SetFloat("_StencilOpFail", 2f);
                    clone.SetFloat("_StencilOpPass", 2f);

                    bool isSkin = skinNames.Any(n => clone.name.Contains(n));
                    clone.SetFloat("_StencilNo", isSkin ? 1f : 2f);

                    if (clone.name.Contains("m_panties"))
                        ___m_hidePantiesEnabled = true;

                    s_karaokeStencilClones.Add(clone);
                    cloned[i] = clone;
                }
                smr.sharedMaterials = cloned;
            }

            ___m_hidePantiesEnabled = false;
            return false;
        }

        // Karaoke 終了時に stencil clone を破棄して元の sharedMaterials を復元する必要はない
        // （Karaoke はキャラ GO を破棄するため renderer ごと消える）。clone の native 資源だけ解放。
        [HarmonyPatch(typeof(Karaoke), "doRelease")]
        [HarmonyPostfix]
        private static void KaraokeRelease_Postfix()
        {
            DestroyKaraokeStencilClones();
        }

        private static void DestroyKaraokeStencilClones()
        {
            if (s_karaokeStencilClones.Count == 0) return;
            int count = s_karaokeStencilClones.Count;
            foreach (var m in s_karaokeStencilClones)
                if (m != null) Object.Destroy(m);
            s_karaokeStencilClones.Clear();
            Plugin.Log.LogInfo($"[MaterialCloneLeakFix] Karaoke stencil clone {count} 個を破棄");
        }
    }
}
