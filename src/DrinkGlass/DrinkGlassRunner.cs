using System.Collections.Generic;
using GB.Scene;
using UnityEngine;
using UnityVRMod.Core;   // VrControllerSnapshot
using BG2VR.VrInput;     // ControllerModelPose / BundledShaders

namespace BG2VR.DrinkGlass
{
    /// <summary>NPC の手持ちグラス（シェル＋飲み物の液体は別 SMR）を BakeMesh で静的複製し、設定手
    /// （rig 子 GO）へ snapshot 追従させて表示する。glass prop ルート（source.transform.parent）配下の
    /// SMR＝glass シェル(source)＋名前に "Liquid" を含む液体メッシュ（Glass06_rig:Liquid7 / Liquid2）を
    /// CombineInstance で 1 メッシュ（submesh 別）に焼き、各 submesh に対応マテリアルを割当てる
    /// （液体を含めないとカクテルの色が出ず空グラスになる）。hand GO へ parent せず独立に snapshot から
    /// ポーズ算出する（ControllerModelRunner と同型）。source==null で非表示。source/種別/マテリアル方式の
    /// 変化で作り直す。fork 非依存・BG2VR 内で完結。</summary>
    internal sealed class DrinkGlassRunner
    {
        private GameObject m_go;
        private MeshFilter m_filter;
        private MeshRenderer m_renderer;
        private Mesh m_combinedMesh;     // 所有（CombineMeshes 生成・rebuild/teardown で破棄）
        private readonly List<Material> m_ownedMats = new List<Material>(); // unlit 近似時のみ所有（実マテリアル時は空）
        private int m_builtSourceId;     // 直近 build した glass SMR の instanceID（0=未 build）
        private CharacterHandle.Props m_builtProp;
        private bool m_builtUseGameMat;
        private bool m_warned;

        /// <summary>VR 非 active / WorldUi 無効時（ProjectorRunner の早期 return）の残像防止。</summary>
        public void HideAll()
        {
            if (m_go != null) m_go.SetActive(false);
        }

        /// <param name="source">複製元の glass シェル SMR（null で非表示）</param>
        /// <param name="useLeftHand">設定手が左か（offset の X 平面ミラー判定）</param>
        public void Tick(Transform rig, in VrControllerSnapshot snap, bool useLeftHand,
            SkinnedMeshRenderer source, CharacterHandle.Props prop)
        {
            if (source == null || rig == null || !snap.Valid)
            {
                if (m_go != null) m_go.SetActive(false);
                return;
            }

            bool useGameMat = Configs.DrinkGlassUseGameMaterial.Value;
            int srcId = source.GetInstanceID();

            // 作り直し: 初回 / source 変化 / プロップ種別変化 / マテリアル方式の live 切替 / GO 道連れ破棄(fake-null)。
            if (m_go == null || m_builtSourceId != srcId || m_builtProp != prop || m_builtUseGameMat != useGameMat)
                Rebuild(source, prop, useGameMat);
            if (m_go == null || m_combinedMesh == null) return; // build 失敗→何も出さない

            if (m_go.transform.parent != rig) m_go.transform.SetParent(rig, false); // rig 差し替え追従

            // offset は右手基準で定義。設定手が左なら X 平面ミラー（既存 ControllerModelPose ヘルパ流用で
            // 単一 config が左右対称に効く）。グラスメッシュ自体は handed でないので scale は反転しない。
            Vector3 posOffset = new Vector3(
                Configs.DrinkGlassPosOffsetX.Value,
                Configs.DrinkGlassPosOffsetY.Value,
                Configs.DrinkGlassPosOffsetZ.Value);
            Quaternion rotOffset = Quaternion.Euler(
                Configs.DrinkGlassRotOffsetX.Value,
                Configs.DrinkGlassRotOffsetY.Value,
                Configs.DrinkGlassRotOffsetZ.Value);
            if (useLeftHand)
            {
                posOffset = new Vector3(-posOffset.x, posOffset.y, posOffset.z);
                rotOffset = ControllerModelPose.MirrorRotationX(rotOffset);
            }

            ControllerModelPose.Compute(snap.RigLocalPosition, snap.RigLocalRotation, posOffset, rotOffset,
                out Vector3 localPos, out Quaternion localRot);
            m_go.transform.localPosition = localPos;
            m_go.transform.localRotation = localRot;
            m_go.transform.localScale = Vector3.one * Configs.DrinkGlassScale.Value;
            m_go.SetActive(true);
        }

        private void Rebuild(SkinnedMeshRenderer source, CharacterHandle.Props prop, bool useGameMat)
        {
            try
            {
                EnsureGo();
                if (m_combinedMesh != null) { Object.Destroy(m_combinedMesh); m_combinedMesh = null; }
                foreach (var m in m_ownedMats) if (m != null) Object.Destroy(m);
                m_ownedMats.Clear();

                // 複製対象 = glass シェル(source) ＋ その配下の液体 SMR（Glass06_rig:Liquid7 はシェルの子）。
                // 液体はカクテルの色を持つ＝一緒に焼かないと空グラスになる。シェル配下に限定するのが要点:
                // prop SMR の親はキャラ root（実機確認）なので、親基準で探すと無関係なカクテルの別シェル配下
                // 液体（Liquid2）まで巻き込む。source 配下なら当該グラスの液体だけを拾う。refRoot もシェル基準。
                // bake 元空間 = rootBone があればその bone 空間、無ければ renderer transform 空間（BakeMesh の仕様）。
                // 共通基準 refRoot もシェルの bake 空間に合わせる（液体を同一フレームへ正しく合成するため）。
                Transform refRoot = source.rootBone != null ? source.rootBone : source.transform;
                var renderers = new List<SkinnedMeshRenderer>();
                foreach (var smr in source.transform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr == source || smr.name.Contains("Liquid")) renderers.Add(smr);

                // 各 SMR を現ポーズで bake（剛体プロップ＝1 度）→ refRoot 相対 transform で 1 メッシュへ結合
                // （submesh 別保持で各 SMR のマテリアルを個別割当）。BakeMesh は SMR ローカル空間前提＝
                // 相対 transform はその前提で算出（※整合は実機で要確認）。
                var combines = new List<CombineInstance>();
                var mats = new List<Material>();
                var temp = new List<Mesh>();
                Vector3 shellAnchor = Vector3.zero; // 再センター基準＝シェル(グラス)の重心。グラス位置を液体から独立させる。
                foreach (var r in renderers)
                {
                    var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    r.BakeMesh(baked);
                    temp.Add(baked);
                    // シェル(rootBone=null)は transform 空間、液体(rootBone=Glass06_rig:stm1)は bone 空間で焼かれる。
                    // 各メッシュをその bake 元空間→refRoot へ写すことで、飲酒ポーズでも glass↔液体の相対位置が保たれる
                    // （transform 一律だと液体だけ bone 空間ぶんズレる＝実機確認）。
                    Transform bakeSpace = r.rootBone != null ? r.rootBone : r.transform;
                    Matrix4x4 rel = refRoot.worldToLocalMatrix * bakeSpace.localToWorldMatrix;
                    if (r == source) shellAnchor = rel.MultiplyPoint(baked.bounds.center); // シェル重心を refRoot 空間で記録
                    Material[] rmats = r.sharedMaterials;
                    Material unlit = null; // renderer ごとの unlit フォールバック（必要時のみ 1 枚生成し所有）
                    for (int s = 0; s < baked.subMeshCount; s++)
                    {
                        // useGameMat: 実マテリアルを使う。取れない submesh は unlit フォールバックへ落として
                        // 無描画（液体が消える）を避ける。useGameMat=false は常に unlit。
                        Material m = useGameMat && rmats != null && rmats.Length > 0
                            ? rmats[Mathf.Min(s, rmats.Length - 1)]
                            : null;
                        if (m == null)
                        {
                            if (unlit == null) { unlit = BuildUnlitMaterial(r); if (unlit != null) m_ownedMats.Add(unlit); }
                            m = unlit; // bundle 未 bake なら null のまま＝その submesh のみ非描画
                        }
                        combines.Add(new CombineInstance { mesh = baked, subMeshIndex = s, transform = rel });
                        mats.Add(m);
                    }
                }

                m_combinedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                m_combinedMesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: false, useMatrices: true);

                // 基準点を「シェル(グラス)の重心」へ正規化する。BakeMesh は NPC の現ポーズ（飲酒で腕を上げた
                // 状態）を SMR/bone 原点相対で焼くため、そのまま置くと NPC の腕の高さぶん（実機で約 50cm）
                // プレイヤーの手より上に浮く。シェル重心を m_go 原点へ寄せ、NPC の腕ポーズに依らずグラス自体を
                // 基準点にする（手の中の微調整は DrinkGlassPosOffset）。**液体でなくシェル基準にするのが要点**:
                // 合成重心だと液体の位置を直すたびグラスまで動く。シェル基準ならグラス位置は液体に依存せず安定し、
                // 液体は同一オフセットで一緒に動くため相対位置は保たれる。
                Vector3 anchor = shellAnchor;
                if (anchor != Vector3.zero)
                {
                    var verts = m_combinedMesh.vertices;
                    for (int i = 0; i < verts.Length; i++) verts[i] -= anchor;
                    m_combinedMesh.vertices = verts;
                    m_combinedMesh.RecalculateBounds();
                }

                foreach (var t in temp) Object.Destroy(t); // combine がコピー済＝中間 bake は破棄
                m_filter.sharedMesh = m_combinedMesh;
                m_renderer.sharedMaterials = mats.ToArray();

                m_builtSourceId = source.GetInstanceID();
                m_builtProp = prop;
                m_builtUseGameMat = useGameMat;
            }
            catch (System.Exception e)
            {
                if (!m_warned) { m_warned = true; Plugin.Log.LogWarning($"[DrinkGlass] 複製に失敗のため非表示: {e}"); }
                if (m_go != null) m_go.SetActive(false);
                // 失敗した入力（source/種別/マテリアル方式）を記録し、同一入力での毎フレ再 bake を抑止する
                // （source 変化/種別変化/方式切替で初めて再試行＝CLAUDE.md「毎フレ重い API を足さない」）。
                // m_combinedMesh は null のまま＝Tick 冒頭で非表示に落ちる。
                m_builtSourceId = source.GetInstanceID();
                m_builtProp = prop;
                m_builtUseGameMat = useGameMat;
            }
        }

        private void EnsureGo()
        {
            if (m_go != null) return;
            m_go = new GameObject("BG2VR_DrinkGlass") { hideFlags = HideFlags.HideAndDontSave };
            SetLayerRecursive(m_go.transform, VrLayers.VisualsPostProcessed); // 手元モデルと同層(29・post 反映)
            m_filter = m_go.AddComponent<MeshFilter>();
            m_renderer = m_go.AddComponent<MeshRenderer>();
            m_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_renderer.receiveShadows = false;
        }

        private static Material BuildUnlitMaterial(SkinnedMeshRenderer source)
        {
            Shader bundled = BundledShaders.ControllerUnlit;
            if (bundled == null) return null; // bundle 未 bake＝呼び出し側で null mat→非描画
            var mat = new Material(bundled) { hideFlags = HideFlags.HideAndDontSave };
            Material src = source.sharedMaterial;
            Texture tex = src != null ? src.mainTexture : null;
            if (tex != null) mat.mainTexture = tex;
            else if (src != null) mat.color = src.color; // テクスチャ無し（液体等）→元の単色（カクテル色）を保つ
            else mat.color = new Color(0.7f, 0.8f, 0.9f, 1f); // それも無ければ淡いガラス色
            // 手元ビジュアルと同じ frontmost queue（UI より手前）。ZTest LEqual で自己オクルージョン正常・
            // 安全側に Cull Off で面欠けを避ける（手/カメラと同方針）。
            mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;
            return mat;
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}
