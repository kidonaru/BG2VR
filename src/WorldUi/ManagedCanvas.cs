using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// 1 つの Canvas を ScreenSpaceCamera 化し、元状態を退避して完全復元する。
    /// 駆動するのは renderMode/worldCamera/planeDistance と root layer、および配下の
    /// alpha-eating material の差し替え（layout は触らない）。
    /// root layer は Default のとき UI(5) へ寄せる（UI カメラ cullingMask に乗せ cull を避ける）。
    /// </summary>
    internal sealed class ManagedCanvas
    {
        private readonly Canvas m_canvas;
        private readonly RenderMode m_origMode;
        private readonly Camera m_origWorldCamera;
        private readonly float m_origPlaneDistance;
        private readonly int m_origLayer;
        private bool m_converted;

        // 乗算 shader（UI/Mulatiply）の Graphic を退避→差し替えた記録。
        // 透明 RT 合成では乗算が dst alpha を潰し world パネル上で UI に穴を開けるため、
        // UI/Default + 一様半透明黒へ近似置換する（UiOverlayRenderPolicy 参照・実測 2026-06-07）。
        // 復元対象は material と color の 2 つで十分（対象は単色 Image 帯のみ。
        // BaseMeshEffect 等の他状態は触らない）。
        private struct SweptGraphic
        {
            public Graphic Graphic;
            public Material OrigMaterial;
            public Color OrigColor;
        }
        private readonly List<SweptGraphic> m_swept = new List<SweptGraphic>();
        private Material m_replacement; // 乗算帯の共有代替 material（UI/Default + 半透明黒・Restore で破棄）
        private Material m_additiveReplacement; // 加算エフェクトの共有代替 material（UiAdditiveKeyed・Restore で破棄）
        private static bool s_warnedNoAdditiveShader; // 同梱 shader 不在の警告（プロセス通算 1 回限り・全 canvas 共通）

        public ManagedCanvas(Canvas canvas)
        {
            m_canvas = canvas;
            m_origMode = canvas.renderMode;
            m_origWorldCamera = canvas.worldCamera;
            m_origPlaneDistance = canvas.planeDistance;
            m_origLayer = canvas.gameObject.layer;
        }

        public void Convert(Camera uiCamera, float planeDistance)
        {
            if (m_converted || m_canvas == null) return;
            m_canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_canvas.worldCamera = uiCamera;
            m_canvas.planeDistance = planeDistance;
            // root layer が Default のとき UI(5) へ寄せる（ComputeCullingMask と同一規則）。
            int eff = CanvasLayerPolicy.EffectiveLayer(m_canvas.gameObject.layer);
            if (eff != m_canvas.gameObject.layer) m_canvas.gameObject.layer = eff;
            SweepAlphaEatingGraphics();
            m_converted = true;
        }

        public void Restore()
        {
            // sweep の復元は canvas-null 早期 return と独立に必ず実行する（canvas GO 破棄済みでも
            // 代替 material は独立資産＝破棄しないとリークする・plan-review 🔴）。
            RestoreSweep();
            if (!m_converted || m_canvas == null) return; // Destroy 済み(fake-null)なら何もしない
            m_canvas.renderMode = m_origMode;
            m_canvas.worldCamera = m_origWorldCamera;
            m_canvas.planeDistance = m_origPlaneDistance;
            m_canvas.gameObject.layer = m_origLayer;
            m_converted = false;
        }

        /// <summary>
        /// 配下の alpha-eating Graphic（現状: GBSystem の Footer/LocationUI 暗化帯）を退避して
        /// 代替 material（UI/Default + 半透明黒）へ差し替える。includeInactive=true（帯はシーン中に
        /// 出入りする）。共有 material 資産は不変更＝平面モードへ影響を残さない。
        /// 変換後に動的生成された該当 Graphic は次の再 Setup まで漏れる（既知の該当なし・許容）。
        /// 検出は materialForRendering（最終描画 shader）・退避/差し替えは material（ベース層）。
        /// 対象帯は mask 非配下（両者の shader 名が一致する）前提＝mask 配下の乗算帯が将来現れたら
        /// 検出/退避の material 層を揃え直すこと（code-review 🟡）。
        /// </summary>
        private void SweepAlphaEatingGraphics()
        {
            foreach (var g in m_canvas.GetComponentsInChildren<Graphic>(true))
            {
                var mat = g.materialForRendering;
                if (mat == null || mat.shader == null) continue;
                string sn = mat.shader.name;

                if (UiOverlayRenderPolicy.IsAlphaEatingShader(sn))
                {
                    if (m_replacement == null)
                    {
                        var shader = Shader.Find("UI/Default");
                        if (shader == null)
                        {
                            // 代替が作れない＝差し替え自体を断念（帯領域の UI 穴は残るが描画は壊さない）。
                            Plugin.Log.LogWarning("[WorldUi] UI/Default が見つからない。乗算帯の alpha 汚染対策をスキップ。");
                            continue;
                        }
                        m_replacement = new Material(shader);
                    }
                    m_swept.Add(new SweptGraphic { Graphic = g, OrigMaterial = g.material, OrigColor = g.color });
                    g.material = m_replacement;
                    g.color = UiOverlayRenderPolicy.AlphaEatingReplacementColor;
                }
                else if (global::BG2VR.Configs.FixVrAdditiveEffects.Value && UiOverlayRenderPolicy.IsAdditiveShader(sn))
                {
                    var addMat = EnsureAdditiveReplacement();
                    if (addMat == null) continue; // 同梱 shader 不在＝補正スキップ（黒矩形は残るが描画は壊さない）
                    // color は変更しない（元の DOFade/tint を維持）。alpha は shader 側で輝度×color.a に変換される。
                    m_swept.Add(new SweptGraphic { Graphic = g, OrigMaterial = g.material, OrigColor = g.color });
                    g.material = addMat;
                }
            }
            if (m_swept.Count > 0)
                Plugin.Log.LogInfo($"[WorldUi] 乗算/加算 {m_swept.Count} 件を VR 安全な代替 material に差し替え（{m_canvas.name}）。");
        }

        /// <summary>加算エフェクト用の共有代替 material（BG2VR/UiAdditiveKeyed）を遅延生成。
        /// 同梱 shader が無い（bake 未済/非対応）ときは null を返し 1 回だけ警告（補正スキップ）。</summary>
        private Material EnsureAdditiveReplacement()
        {
            if (m_additiveReplacement != null) return m_additiveReplacement;
            var sh = BG2VR.VrInput.BundledShaders.UiAdditiveKeyed;
            if (sh == null)
            {
                if (!s_warnedNoAdditiveShader)
                {
                    s_warnedNoAdditiveShader = true;
                    Plugin.Log.LogWarning("[WorldUi] UiAdditiveKeyed shader が無い＝加算エフェクトの黒矩形補正をスキップ（bundle 未 bake の可能性）。");
                }
                return null;
            }
            m_additiveReplacement = new Material(sh);
            return m_additiveReplacement;
        }

        private void RestoreSweep()
        {
            foreach (var s in m_swept)
            {
                if (s.Graphic == null) continue; // Graphic 破棄済み(fake-null)は復元 skip（material 破棄は下で必ず行う）
                s.Graphic.material = s.OrigMaterial;
                s.Graphic.color = s.OrigColor;
            }
            m_swept.Clear();
            // 代替 material はエントリ有無に依らず必ず破棄（graphic/canvas 破棄済みでもリークさせない）。
            if (m_replacement != null) { Object.Destroy(m_replacement); m_replacement = null; }
            if (m_additiveReplacement != null) { Object.Destroy(m_additiveReplacement); m_additiveReplacement = null; }
        }
    }
}
