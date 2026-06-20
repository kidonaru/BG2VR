using UnityEngine;
using BG2VR.VrInput;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// パネル下のボタン quad×3（移動/拡大/曲面）。VrUiPanel root の子（scale=1 空間）に実寸配置し
    /// 正方形を維持する。レイヤ=VrLayers.Visuals(30)＝eye 可視・UI カメラ非混入・UiSceneVoid 中も可視
    /// （VrUiPanel と同方針）。
    /// 表示はレーザー命中中のみ（renderer.enabled 切替＝GO は常設で live トグルに即応）。
    /// </summary>
    internal sealed class PanelButtonBar
    {
        private static readonly Color NormalTint = new Color(0.72f, 0.72f, 0.78f, 1f);
        private static readonly Color HoverTint = Color.white;
        private static readonly Color DragTint = new Color(0.45f, 0.85f, 1f, 1f);
        private static readonly Color CurveOnTint = new Color(0.45f, 0.85f, 1f, 1f);

        private readonly GameObject[] m_buttons = new GameObject[ButtonBarLayout.ButtonCount];
        private readonly MeshRenderer[] m_renderers = new MeshRenderer[ButtonBarLayout.ButtonCount];
        private readonly Material[] m_materials = new Material[ButtonBarLayout.ButtonCount];
        private readonly Texture2D[] m_icons = new Texture2D[ButtonBarLayout.ButtonCount];
        private float m_side; // 現在のボタン辺(m)・raycast 用
        private bool m_visible;

        /// <summary>現在のボタン辺(m)（拡張矩形の計算用に公開・Layout 前は 0）。</summary>
        public float CurrentSide => m_side;

        private static readonly PanelButtonKind[] Kinds =
            { PanelButtonKind.Move, PanelButtonKind.Scale, PanelButtonKind.Curve };

        public void Create(Transform parentRoot)
        {
            // UI/Default（ZTest 制御可）→ Sprites/Default の null ガード（VrUiPanel と同方針・
            // 旧 URP/Unlit 系候補は本ビルドに存在しないため削除・実測 2026-06-07）。
            Shader shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                Plugin.Log.LogWarning("[WorldUi] UI/Default が見つからない。ボタンは Sprites/Default に fallback（depth test 設定は無効）。");
                shader = Shader.Find("Sprites/Default");
            }
            for (int i = 0; i < m_buttons.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = $"BG2VR_UiButton_{Kinds[i]}";
                go.hideFlags = HideFlags.HideAndDontSave;
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col); // raycast は自前（QuadRaycaster）＝物理 collider 不要
                go.layer = VrLayers.Visuals; // UiSceneVoid 中も eye から見える専用 layer

                m_icons[i] = new Texture2D(IconPainter.Size, IconPainter.Size, TextureFormat.RGBA32, false);
                m_icons[i].SetPixels32(IconPainter.Paint(Kinds[i]));
                m_icons[i].Apply(false, true);

                var renderer = go.GetComponent<MeshRenderer>();
                if (shader != null)
                {
                    m_materials[i] = new Material(shader);
                    m_materials[i].mainTexture = m_icons[i];
                    renderer.sharedMaterial = m_materials[i];
                }
                else
                {
                    Plugin.Log.LogError("[WorldUi] ボタン用 UI シェーダが見つからない。既定マテリアルで継続。");
                    m_materials[i] = renderer.material; // インスタンス化されたマテリアルを保持（Destroy で明示破棄＝リーク防止）
                    m_materials[i].mainTexture = m_icons[i];
                }
                // パネル(4000)より常に後＝重なってもボタンが見える + 実効 depth test（パネルと同挙動・fallback 分岐でも適用）。
                UiOverlayRenderPolicy.Apply(m_materials[i], UiOverlayRenderPolicy.ButtonQueue,
                    UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Button,
                        global::BG2VR.Configs.VrControllerOccludeUi.Value, global::BG2VR.Configs.WorldUiDepthTest.Value));
                renderer.enabled = false; // 初期非表示
                m_renderers[i] = renderer;

                go.transform.SetParent(parentRoot, false);
                m_buttons[i] = go;
            }
        }

        /// <summary>パネル実寸・ボタン辺(m)に合わせて位置・サイズを更新（VrUiPanel から）。</summary>
        public void Layout(float panelWidth, float panelHeight, float side)
        {
            m_side = side;
            for (int i = 0; i < m_buttons.Length; i++)
            {
                if (m_buttons[i] == null) continue;
                m_buttons[i].transform.localPosition = ButtonBarLayout.ButtonCenter(i, panelWidth, panelHeight, side);
                m_buttons[i].transform.localScale = new Vector3(side, side, 1f);
            }
        }

        public void SetVisible(bool visible)
        {
            if (m_visible == visible) return;
            m_visible = visible;
            foreach (var r in m_renderers) { if (r != null) r.enabled = visible; }
        }

        /// <summary>depth test 設定の live 反映（VrUiPanel.ApplyDepthTest から伝播）。</summary>
        public void ApplyDepthTest(bool depthTest)
        {
            foreach (var m in m_materials)
                UiOverlayRenderPolicy.Apply(m, UiOverlayRenderPolicy.ButtonQueue, depthTest);
        }

        /// <summary>hover/ドラッグ/曲面 ON の表示状態を反映（tint は material.color
        ///＝UI/Default・Sprites/Default とも _Color にマップされ有効）。</summary>
        public void SetTints(PanelButtonKind hover, PanelButtonKind dragging, bool curveOn)
        {
            for (int i = 0; i < m_materials.Length; i++)
            {
                if (m_materials[i] == null) continue;
                Color c = NormalTint;
                if (Kinds[i] == PanelButtonKind.Curve && curveOn) c = CurveOnTint;
                if (Kinds[i] == hover) c = HoverTint;
                if (Kinds[i] == dragging) c = DragTint;
                m_materials[i].color = c;
            }
        }

        /// <summary>
        /// 凍結 ray でボタンを raycast。最初に命中した種別と world 交点を返す（非重複配置＝順序不問）。
        /// normal にはパネル本体（ProcessUi）・拡張矩形と同じく forward を渡す。QuadRaycaster は
        /// normal の符号に不感（t の分子分母で相殺・u/v は right/up のみ依存）のため、3 箇所で
        /// 軸が揃っていれば読み面の向きに関わらず安全（plan-review 🔴1）。
        /// </summary>
        public PanelButtonKind RaycastButtons(Vector3 origin, Vector3 dir, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.zero; // 未命中時は未使用（呼び出し側契約）
            if (m_side <= 0f) return PanelButtonKind.None; // Layout 前（Setup で必ず先行するが不変条件を明示）
            for (int i = 0; i < m_buttons.Length; i++)
            {
                var t = m_buttons[i] != null ? m_buttons[i].transform : null;
                if (t == null) continue;
                // quad primitive の実ワールド半辺（rig scale 込み）。m_side は rig-local 値のため
                // 直接使わない（WorldScale≠1 で当たり判定が見た目とずれる）。
                float half = 0.5f * t.lossyScale.x;
                // rtWidth/rtHeight=1 のダミー（Pixel は使わない・Valid と WorldPoint のみ）
                var hit = QuadRaycaster.Raycast(origin, dir, t.position, t.right, t.up, t.forward, half, half, 1, 1);
                if (hit.Valid)
                {
                    hitPoint = hit.WorldPoint;
                    hitNormal = hit.Normal;
                    return Kinds[i];
                }
            }
            return PanelButtonKind.None;
        }

        public void Destroy()
        {
            for (int i = 0; i < m_buttons.Length; i++)
            {
                if (m_buttons[i] != null) Object.Destroy(m_buttons[i]);
                if (m_materials[i] != null) Object.Destroy(m_materials[i]);
                if (m_icons[i] != null) Object.Destroy(m_icons[i]);
                m_buttons[i] = null; m_renderers[i] = null; m_materials[i] = null; m_icons[i] = null;
            }
            m_visible = false;
        }
    }
}
