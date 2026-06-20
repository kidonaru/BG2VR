using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// RenderTexture を貼る world パネル（旧 VrUiQuad 後継）。rig の子（worldScale 継承・teardown 道連れ破棄）。
    /// 構造: root GO（姿勢のみ・scale=1）+ mesh 子（実寸メッシュ・PanelMeshBuilder）+ ボタン帯 子。
    /// 非等方 localScale をやめ実寸メッシュ再生成にしたのは、曲面（x,z 双方に曲率）とボタン正方形維持の
    /// 両立のため（spec §3）。レイヤ=VrLayers.Visuals(30): eye は描画（mask 所有は EyeCullingCoordinator）・
    /// UI カメラ mask（canvas root layer 和集合）は 30 を含まず RT 非混入・UiSceneVoid 中も可視。
    /// </summary>
    internal sealed class VrUiPanel
    {
        private GameObject m_root;
        private GameObject m_meshGo;
        private Mesh m_mesh;
        private Material m_material;
        private PanelButtonBar m_buttons;
        private float m_width;   // 実寸キャッシュ（0 以下=未確定）
        private float m_eyeDistance; // 視点→パネル距離キャッシュ(m)。ボタン視角一定スケール用（0=未確定）
        private bool m_curved;
        private float m_radius = 1f;

        public bool Exists => m_root != null;
        public Transform RootTransform => m_root != null ? m_root.transform : null;
        public float Width => m_width;
        public float Height => m_width * (PlacementSolver.RefHeightPx / PlacementSolver.RefWidthPx);
        public bool Curved => m_curved;
        public float Radius => m_radius;
        public PanelButtonBar Buttons => m_buttons;

        public void Create(Transform rig, RenderTexture rt)
        {
            m_root = new GameObject("BG2VR_UiPanel");
            m_root.hideFlags = HideFlags.HideAndDontSave;
            m_root.transform.SetParent(rig, false);

            m_meshGo = new GameObject("BG2VR_UiPanelMesh");
            m_meshGo.hideFlags = HideFlags.HideAndDontSave;
            m_meshGo.layer = VrLayers.Visuals; // UiSceneVoid 中も eye から見える専用 layer
            m_meshGo.transform.SetParent(m_root.transform, false);

            m_mesh = new Mesh();
            m_mesh.name = "BG2VR_UiPanelMesh";
            m_meshGo.AddComponent<MeshFilter>().sharedMesh = m_mesh;

            // UI/Default で RT を貼る（unity_GUIZTestMode で ZTest を制御できる唯一の既存 shader。
            // 旧候補の URP/Unlit 系は本ビルドに存在しない＝常に Sprites/Default（queue=3000 固定）に
            // 落ちて前髪 over-pass(q=3010) に上書きされていた・実測 2026-06-07）。
            Shader shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                // fallback: queue は載るが ZTest は LEqual 固定（unity_GUIZTestMode 非対応）＝機能劣化のみ。
                Plugin.Log.LogWarning("[WorldUi] UI/Default が見つからない。Sprites/Default に fallback（depth test 設定は無効）。");
                shader = Shader.Find("Sprites/Default");
            }
            var renderer = m_meshGo.AddComponent<MeshRenderer>();
            if (shader != null)
            {
                m_material = new Material(shader);
                m_material.mainTexture = rt;
                renderer.sharedMaterial = m_material;
            }
            else
            {
                // 想定外（全候補 strip）。crash は避けつつ原因を明示（実機で真っ黒/マゼンタを踏む前に切り分け可能に）。
                Plugin.Log.LogError("[WorldUi] UI シェーダが見つからない。既定マテリアルに RT を載せる。");
                m_material = renderer.material; // インスタンス化されたマテリアルを保持（Destroy で明示破棄＝リーク防止）
                m_material.mainTexture = rt;
            }
            // 前髪 over-pass(q=3010) より上 + 実効 depth test（遮蔽 ON でコントローラに隠れる／OFF は WorldUiDepthTest）。fallback 分岐でも適用。
            UiOverlayRenderPolicy.Apply(m_material, UiOverlayRenderPolicy.PanelQueue,
                UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Panel,
                    global::BG2VR.Configs.VrControllerOccludeUi.Value, global::BG2VR.Configs.WorldUiDepthTest.Value));

            m_buttons = new PanelButtonBar();
            m_buttons.Create(m_root.transform);
        }

        /// <summary>姿勢の直接適用（rig-local・root へ）。既定配置・移動ドラッグの両方がこれを使う
        /// （回転は呼び出し側で構築済み・常に rig 軸相対＝spec §5 系の既存規約）。</summary>
        public void ApplyPose(Vector3 localPos, Quaternion localRot)
        {
            if (m_root == null) return;
            m_root.transform.localPosition = localPos;
            m_root.transform.localRotation = localRot;
        }

        /// <summary>パネル幅(m)を適用（メッシュ実寸再生成 + 帯再レイアウト）。同値なら no-op。</summary>
        public void ApplyScale(float sizeScale)
        {
            if (m_root == null || sizeScale <= 0f) return;
            if (Mathf.Approximately(m_width, sizeScale)) return; // 未配置フェーズの毎フレ呼出を no-op 化
            m_width = sizeScale;
            RebuildMesh();
            LayoutButtons();
        }

        /// <summary>視点位置（rig-local）からボタンの視角一定スケールを更新する。
        /// 姿勢適用後に呼ぶ（距離が変わるのは pose 変更時のみ・spec §2）。</summary>
        public void UpdateEyeDistance(Vector3 eyeLocalPos)
        {
            if (m_root == null) return;
            float d = (m_root.transform.localPosition - eyeLocalPos).magnitude;
            if (Mathf.Approximately(d, m_eyeDistance)) return;
            m_eyeDistance = d;
            LayoutButtons();
        }

        /// <summary>ボタンサイズ比率 config の live 反映用（距離・幅が不変でも帯を再レイアウト）。</summary>
        public void RefreshButtonLayout() => LayoutButtons();

        /// <summary>depth test 設定の live 反映（F10 トグル → ProjectorRunner watch から。ボタン帯へも伝播）。</summary>
        public void ApplyDepthTest(bool depthTest)
        {
            UiOverlayRenderPolicy.Apply(m_material, UiOverlayRenderPolicy.PanelQueue, depthTest);
            m_buttons?.ApplyDepthTest(depthTest);
        }

        private void LayoutButtons()
        {
            if (m_width <= 0f) return; // 幅未確定（Create 直後）は ApplyScale を待つ
            // ApplyScale からも呼ぶのは「サイズ watch 単独経路（UpdateEyeDistance を伴わない）でも
            // 帯が追従する」ため。配置時は直後の UpdateEyeDistance が正距離で再レイアウトする（軽量・3 quad）。
            m_buttons?.Layout(m_width, Height, ButtonBarLayout.ButtonSide(
                m_eyeDistance,
                global::BG2VR.Configs.WorldUiButtonSizeRatio.Value,
                global::BG2VR.Configs.WorldUiButtonSizeOffset.Value));
        }

        /// <summary>曲面状態の適用（同値なら no-op）。</summary>
        public void SetCurved(bool curved, float radius)
        {
            radius = Mathf.Max(0.01f, radius);
            if (m_curved == curved && Mathf.Approximately(m_radius, radius)) return;
            m_curved = curved;
            m_radius = radius;
            RebuildMesh();
        }

        private void RebuildMesh()
        {
            if (m_mesh == null || m_width <= 0f) return; // 幅未確定（Create 直後）は ApplyScale を待つ
            var data = m_curved
                ? PanelMeshBuilder.BuildCurved(m_width, Height, m_radius)
                : PanelMeshBuilder.BuildFlat(m_width, Height);
            m_mesh.Clear();
            m_mesh.vertices = data.Vertices;
            m_mesh.uv = data.Uvs;
            m_mesh.triangles = data.Triangles;
            m_mesh.RecalculateBounds();
        }

        public void Destroy()
        {
            if (m_buttons != null) { m_buttons.Destroy(); m_buttons = null; }
            if (m_root != null) Object.Destroy(m_root);
            m_root = null; m_meshGo = null;
            if (m_mesh != null) Object.Destroy(m_mesh);
            m_mesh = null;
            if (m_material != null) Object.Destroy(m_material);
            m_material = null;
            m_width = 0f;
            m_eyeDistance = 0f;
        }
    }
}
