using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BG2VR.HeadLook
{
    /// <summary>
    /// mesh_eye の uv0 を base+offset で書き換えて虹彩を動かす per-char ヘルパ（spec §4 方式 D）。
    /// T0 実測（spec §2.1 ③）: +U=キャラの右 / +V=下 / ±0.10 まで破綻なし・isReadable=true。
    /// sharedMesh へ clone を代入する前に FixMod NativeSmrRegistry.GetOrCapture を reflection で
    /// best-effort 呼び出しする（CLAUDE.md 規約。FixMod 不在なら no-op・FreeCameraVrGuard と同パターン）。
    /// </summary>
    internal sealed class EyeUvDriver
    {
        private const string EyeSmrName = "mesh_eye";
        private const string CloneSuffix = "_bg2vrEyeLook"; // 由来識別用（MOD 生成 mesh と分かる名前）

        private SkinnedMeshRenderer m_smr;
        private Mesh m_orig;
        private Mesh m_clone;
        private Vector2[] m_baseUv;
        private Vector2[] m_workUv;
        private Vector2 m_lastOffset;

        // FixMod NativeSmrRegistry.GetOrCapture(GameObject, SkinnedMeshRenderer)。不在なら null。
        private static MethodInfo s_getOrCapture;
        private static bool s_searched;

        // m_orig も見る: 衣装 swap 等で mesh だけ破棄されたら無効（fake-null。再レビュー 🟡#1）
        public bool IsValid => m_smr != null && m_orig != null;

        /// <summary>chara 配下の mesh_eye を解決する。不在/非 readable なら IsValid=false（目だけ skip）。</summary>
        public void Resolve(GameObject chara)
        {
            Restore(); // 旧 instance の clone を後始末してから再解決（衣装 reload）
            m_smr = null; m_orig = null; m_baseUv = null; m_workUv = null;
            foreach (var smr in chara.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name == EyeSmrName) { m_smr = smr; break; }
            }
            if (m_smr == null) return;
            m_orig = m_smr.sharedMesh;
            if (m_orig == null || !m_orig.isReadable)
            {
                Plugin.Log.LogWarning($"[HeadLook] {EyeSmrName} が解決できません（mesh null/非readable）: {chara.name}");
                m_smr = null;
            }
        }

        /// <summary>uv offset を適用（初回は clone 生成）。offset: +x=キャラの右 / +y=下。</summary>
        public void Apply(GameObject chara, Vector2 offset)
        {
            if (m_smr == null) return;
            // CostumeChanger 等が同一 Chara のまま mesh を破棄した場合の防御
            // （Instantiate(破棄済み) を踏まない。再レビュー 🟡#1）
            if (m_orig == null) { Restore(); m_smr = null; return; }
            if (m_clone == null)
            {
                if (offset.sqrMagnitude < 1e-10f) return; // ゼロのまま: clone 不要
                CallGetOrCapture(chara, m_smr);
                m_clone = UnityEngine.Object.Instantiate(m_orig);
                m_clone.name = m_orig.name + CloneSuffix;
                m_clone.MarkDynamic(); // 毎フレ uv 更新前提
                m_baseUv = m_clone.uv;
                m_workUv = new Vector2[m_baseUv.Length];
                m_smr.sharedMesh = m_clone;
                m_lastOffset = new Vector2(float.NaN, float.NaN); // 初回は必ず書く
            }
            if ((offset - m_lastOffset).sqrMagnitude < 1e-10f) return; // 変化なし: mesh 更新を省略
            for (int i = 0; i < m_baseUv.Length; i++)
                m_workUv[i] = m_baseUv[i] + offset;
            m_clone.uv = m_workUv;
            m_lastOffset = offset;
        }

        /// <summary>元 mesh へ復元し clone を破棄（OFF/遷移/衣装 reload/破棄時）。</summary>
        public void Restore()
        {
            // Unity fake-null: smr が破棄済みなら代入 skip（clone の破棄だけ行う）
            if (m_smr != null && m_clone != null)
                m_smr.sharedMesh = m_orig;
            if (m_clone != null) UnityEngine.Object.Destroy(m_clone);
            m_clone = null;
            m_baseUv = null;
            m_workUv = null;
        }

        private static void CallGetOrCapture(GameObject chara, SkinnedMeshRenderer smr)
        {
            if (!s_searched)
            {
                s_searched = true;
                var t = AccessTools.TypeByName(
                    "BunnyGarden2FixMod.Patches.CostumeChanger.Internal.NativeSmrRegistry");
                if (t != null) s_getOrCapture = AccessTools.Method(t, "GetOrCapture");
                Plugin.Log.LogInfo(s_getOrCapture != null
                    ? "[HeadLook] NativeSmrRegistry 連携あり（FixMod 検出）"
                    : "[HeadLook] NativeSmrRegistry なし（FixMod 不在 or 型変更）= 単独動作");
            }
            try { s_getOrCapture?.Invoke(null, new object[] { chara, smr }); }
            catch (Exception e) { Plugin.Log.LogWarning($"[HeadLook] GetOrCapture 呼出失敗: {e.Message}"); }
        }
    }
}
