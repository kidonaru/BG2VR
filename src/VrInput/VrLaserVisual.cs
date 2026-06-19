using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// レーザー線（LineRenderer・先細りテーパー + 終端アルファフェード）+ hit レティクル
    /// （円形ソフトリング quad・ヒット面に沿わせ・距離×比率の視角一定サイズ）。
    /// レイヤ: 線・レティクルとも VrLayers.Visuals(30)＝crisp overlay（post 非反映・常に最前面のポインタ）。
    /// eye 可視・UI カメラ非混入・UiSceneVoid 中も可視（VrUiPanel と同方針）。
    /// shader は UI/Default（ZTest 制御可・頂点カラー対応）→ Sprites/Default の null ガード（VrUiPanel 同方針）。
    /// Gradient は alloc 回避のため (hit 有無, VrLaserWidth) の変化フレームのみ再構築（F10 live 反映は維持）。
    /// spec: docs/superpowers/specs/2026-06-13-bg2-vr-laser-visual-and-click-alignment-design.md §4
    /// </summary>
    internal sealed class VrLaserVisual
    {
        private static readonly Color LaserColor = new Color(0.2f, 0.8f, 1f, 1f);
        private const float TaperEndRatio = 0.35f; // 終端の太さ比（先細り）
        private const float StartAlpha = 0.9f;
        private const float HitEndAlpha = 0.35f;   // ヒット時終端アルファ（レティクルへ視覚的に引き継ぐ）
        private const float ReticleLift = 0.002f;  // Z-fight 回避リフト(m)・構造定数

        private GameObject m_lineGo;
        private LineRenderer m_line;
        private GameObject m_reticle;
        private Material m_mat;        // 線用（色は gradient 側・material は白）
        private Material m_reticleMat; // レティクル用（リングテクスチャ + 着色）
        private Texture2D m_reticleTex;
        // Gradient 再構築判定キャッシュ（-1 = 未設定）
        private float m_cachedWidth = -1f;
        private int m_cachedHitState = -1;
        private int m_queue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue; // 既定=ゲーム UI レーザー

        public void Create(Transform parent, int renderQueue = -1)
        {
            if (renderQueue >= 0) m_queue = renderQueue;

            Shader shader = Shader.Find("UI/Default");
            if (shader == null)
            {
                Plugin.Log.LogWarning("[VrInput] UI/Default が見つからない。レーザーは Sprites/Default に fallback（depth test 設定は無効）。");
                shader = Shader.Find("Sprites/Default");
            }

            m_mat = shader != null ? new Material(shader) : null;
            // 線の色・アルファは LineRenderer.colorGradient（頂点カラー）で与える＝material は白のまま。
            // ポインタは遮蔽 ON でも常に最前面（Always）。
            BG2VR.WorldUi.UiOverlayRenderPolicy.Apply(m_mat, m_queue,
                BG2VR.WorldUi.UiOverlayRenderPolicy.EffectiveDepthTest(BG2VR.WorldUi.UiOverlayKind.Laser,
                    global::BG2VR.Configs.VrControllerOccludeUi.Value, global::BG2VR.Configs.WorldUiDepthTest.Value));

            m_lineGo = new GameObject("BG2VR_Laser");
            m_lineGo.hideFlags = HideFlags.HideAndDontSave;
            m_lineGo.transform.SetParent(parent, false);
            m_lineGo.layer = VrLayers.Visuals; // 線も crisp overlay 層（ポインタは常に最前面・post 非反映・UiSceneVoid 中も可視）
            m_line = m_lineGo.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.positionCount = 2;
            m_line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, TaperEndRatio); // 先細り（実太さは widthMultiplier）
            if (m_mat != null) m_line.sharedMaterial = m_mat;

            m_reticle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_reticle.name = "BG2VR_Reticle";
            m_reticle.hideFlags = HideFlags.HideAndDontSave;
            // rig サブツリーに置く（線と同じ parent）。post overlay は rig 配下の layer 30 のみ重ね描くため、
            // 未 parent だと main pass から除外されたまま overlay にも入らず非描画になる（位置/回転は world で毎フレ設定）。
            m_reticle.transform.SetParent(parent, false);
            var col = m_reticle.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            m_reticle.layer = VrLayers.Visuals;
            m_reticleTex = new Texture2D(ReticlePainter.Size, ReticlePainter.Size, TextureFormat.RGBA32, false);
            m_reticleTex.SetPixels32(ReticlePainter.Paint());
            m_reticleTex.Apply(false, true);
            var rr = m_reticle.GetComponent<MeshRenderer>();
            if (shader != null)
            {
                m_reticleMat = new Material(shader);
                m_reticleMat.mainTexture = m_reticleTex;
                m_reticleMat.color = LaserColor;
                rr.sharedMaterial = m_reticleMat;
            }
            else
            {
                m_reticleMat = rr.material; // インスタンス化を保持（Destroy で明示破棄＝リーク防止）
                m_reticleMat.mainTexture = m_reticleTex;
            }
            BG2VR.WorldUi.UiOverlayRenderPolicy.Apply(m_reticleMat, m_queue,
                BG2VR.WorldUi.UiOverlayRenderPolicy.EffectiveDepthTest(BG2VR.WorldUi.UiOverlayKind.Reticle,
                    global::BG2VR.Configs.VrControllerOccludeUi.Value, global::BG2VR.Configs.WorldUiDepthTest.Value));
        }

        /// <param name="hitNormal">ヒット面の法線（world・ray 始点側を向く）。hit=false 時は未使用。</param>
        public void UpdateVisual(bool visible, Vector3 origin, Vector3 dir, bool hit, Vector3 hitPoint, Vector3 hitNormal)
        {
            if (m_lineGo != null)
            {
                m_lineGo.SetActive(visible);
                if (visible && m_line != null)
                {
                    EnsureLineStyle(hit);
                    Vector3 end = hit
                        ? hitPoint
                        : origin + dir.normalized * global::BG2VR.Configs.VrLaserMissLength.Value;
                    m_line.SetPosition(0, origin);
                    m_line.SetPosition(1, end);
                }
            }
            if (m_reticle != null)
            {
                m_reticle.SetActive(visible && hit);
                if (visible && hit)
                {
                    // 面沿い配置（Quad の可視面 -Z を法線方向＝視点側へ向ける）+ Z-fight 回避リフト。
                    m_reticle.transform.position = hitPoint + hitNormal * ReticleLift;
                    Vector3 up = Mathf.Abs(hitNormal.y) > 0.99f ? Vector3.forward : Vector3.up;
                    m_reticle.transform.rotation = Quaternion.LookRotation(-hitNormal, up);
                    // 視角一定サイズ（距離 × 比率）。rig サブツリーに置いたので parent の lossyScale
                    //（rig=1/WorldScale）を打ち消して world 実寸を side に保つ。
                    float side = (hitPoint - origin).magnitude * global::BG2VR.Configs.VrReticleSizeRatio.Value;
                    Transform rp = m_reticle.transform.parent;
                    float pl = rp != null ? rp.lossyScale.x : 1f;
                    float local = Mathf.Approximately(pl, 0f) ? side : side / pl;
                    m_reticle.transform.localScale = new Vector3(local, local, 1f);
                }
            }
        }

        // (hit 有無, 太さ config) が変わったフレームだけ widthMultiplier と Gradient を再構築。
        private void EnsureLineStyle(bool hit)
        {
            float width = global::BG2VR.Configs.VrLaserWidth.Value;
            int hitState = hit ? 1 : 0;
            if (width == m_cachedWidth && hitState == m_cachedHitState) return;
            m_cachedWidth = width;
            m_cachedHitState = hitState;

            m_line.widthMultiplier = width;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(LaserColor, 0f), new GradientColorKey(LaserColor, 1f) },
                new[]
                {
                    new GradientAlphaKey(StartAlpha, 0f),
                    // ヒット時はレティクルへ引き継ぐ薄さまで、非ヒット時は 0 までフェード（ぶつ切りなし）。
                    new GradientAlphaKey(hit ? HitEndAlpha : 0f, 1f),
                });
            m_line.colorGradient = g;
        }

        /// <summary>depth test 設定の live 反映（ProjectorRunner watch → VrPointerRunner から）。
        /// 線とレティクルで実効値が異なる（線=遮蔽 ON でコントローラに隠れる / レティクル=常に最前面）ため
        /// 種別ごとに解決する（Create 時と同じロジック）。</summary>
        public void ApplyDepthTest()
        {
            bool occlude = global::BG2VR.Configs.VrControllerOccludeUi.Value;
            bool world = global::BG2VR.Configs.WorldUiDepthTest.Value;
            BG2VR.WorldUi.UiOverlayRenderPolicy.Apply(m_mat, m_queue,
                BG2VR.WorldUi.UiOverlayRenderPolicy.EffectiveDepthTest(BG2VR.WorldUi.UiOverlayKind.Laser, occlude, world));
            BG2VR.WorldUi.UiOverlayRenderPolicy.Apply(m_reticleMat, m_queue,
                BG2VR.WorldUi.UiOverlayRenderPolicy.EffectiveDepthTest(BG2VR.WorldUi.UiOverlayKind.Reticle, occlude, world));
        }

        public void Destroy()
        {
            if (m_lineGo != null) Object.Destroy(m_lineGo);
            if (m_reticle != null) Object.Destroy(m_reticle);
            if (m_mat != null) Object.Destroy(m_mat);
            if (m_reticleMat != null) Object.Destroy(m_reticleMat);
            if (m_reticleTex != null) Object.Destroy(m_reticleTex);
            m_lineGo = null; m_line = null; m_reticle = null;
            m_mat = null; m_reticleMat = null; m_reticleTex = null;
            m_cachedWidth = -1f;
            m_cachedHitState = -1;
        }
    }
}
