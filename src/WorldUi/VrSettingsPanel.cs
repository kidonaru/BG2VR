using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// 設定パネル(F10・UI Toolkit)の targetTexture(RT) を貼る world パネル（rig 子・layer Visuals(30)）。
    /// ゲーム UI パネル(VrUiPanel)に**重ねて合成**するため、pose/サイズ/曲面を呼び出し側(SettingsPanelRunner)が
    /// gamePanel から継承して与える。RT は透明背景・設定は右上ネイティブ位置のまま＝デスクトップ一致の合成。
    /// VrUiPanel との違い: 調整ボタン帯なし / クロップなし(RT 全面を 16:9 で貼る) /
    /// **前面化は `SettingsOverlayQueue` + depthTest=false(ZTest Always) 固定**（手前オフセット不要・曲面端の z-fight 回避）。
    /// material/mesh は VrUiPanel と同規約（UI/Default + UiOverlayRenderPolicy・PanelMeshBuilder.BuildFlat/BuildCurved）。
    /// </summary>
    internal sealed class VrSettingsPanel
    {
        private GameObject m_root;
        private GameObject m_meshGo;
        private Mesh m_mesh;
        private Material m_material;
        private float m_width;   // 物理幅(m)。0 以下=未確定
        private bool m_curved;
        private float m_radius = 1f;

        public bool Exists => m_root != null;
        public Transform RootTransform => m_root != null ? m_root.transform : null;
        public float Width => m_width;
        // 高さは 16:9 固定（ゲーム合成 RT と同マッピング＝設定が右上に出る位置がゲーム UI と揃う）。
        public float Height => m_width * (PlacementSolver.RefHeightPx / PlacementSolver.RefWidthPx);
        public bool Curved => m_curved;     // raycast 用（SettingsPanelRunner.ProcessLaser）
        public float Radius => m_radius;

        public void Create(Transform rig, RenderTexture rt)
        {
            m_root = new GameObject("BG2VR_SettingsPanel");
            m_root.hideFlags = HideFlags.HideAndDontSave;
            m_root.transform.SetParent(rig, false);

            m_meshGo = new GameObject("BG2VR_SettingsPanelMesh");
            m_meshGo.hideFlags = HideFlags.HideAndDontSave;
            m_meshGo.layer = VrLayers.Visuals; // UiSceneVoid 中も eye から見える専用 layer
            m_meshGo.transform.SetParent(m_root.transform, false);

            m_mesh = new Mesh { name = "BG2VR_SettingsPanelMesh" };
            m_meshGo.AddComponent<MeshFilter>().sharedMesh = m_mesh;

            // VrUiPanel と同じ shader 選択規約（UI/Default が唯一 ZTest 制御可能・Unlit 系は strip 済）。
            Shader shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                Plugin.Log.LogWarning("[SettingsPanel] UI/Default が見つからない。Sprites/Default に fallback。");
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
                // 想定外（全候補 strip）。crash は避けつつ原因を明示。
                Plugin.Log.LogError("[SettingsPanel] UI シェーダが見つからない。既定マテリアルに RT を載せる。");
                m_material = renderer.material; // インスタンス化されたマテリアルを保持（Destroy で明示破棄＝リーク防止）
                m_material.mainTexture = rt;
            }
            // 設定はモーダル合成＝常に最前面（Settings 種別は occlude/worldDepthTest に依らず ZTest Always）+ ゲームパネルより上の queue。
            UiOverlayRenderPolicy.Apply(m_material, UiOverlayRenderPolicy.SettingsOverlayQueue,
                UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Settings,
                    global::BG2VR.Configs.VrControllerOccludeUi.Value, global::BG2VR.Configs.WorldUiDepthTest.Value));
        }

        /// <summary>RT を貼り替える（再生成せずテクスチャだけ差替え）。</summary>
        public void SetTexture(RenderTexture rt)
        {
            if (m_material != null) m_material.mainTexture = rt;
        }

        public void ApplyPose(Vector3 localPos, Quaternion localRot)
        {
            if (m_root == null) return;
            m_root.transform.localPosition = localPos;
            m_root.transform.localRotation = localRot;
        }

        /// <summary>物理幅(m)を適用（高さは 16:9 自動・mesh 再生成）。同値なら no-op。</summary>
        public void ApplyScale(float width)
        {
            if (m_root == null || width <= 0f) return;
            if (Mathf.Approximately(m_width, width)) return;
            m_width = width;
            RebuildMesh();
        }

        /// <summary>曲面状態の適用（gamePanel に追従・同値なら no-op）。</summary>
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
            if (m_root != null) Object.Destroy(m_root);
            m_root = null; m_meshGo = null;
            if (m_mesh != null) Object.Destroy(m_mesh);
            m_mesh = null;
            if (m_material != null) Object.Destroy(m_material);
            m_material = null;
            m_width = 0f;
            m_curved = false;
            m_radius = 1f;
        }
    }
}
