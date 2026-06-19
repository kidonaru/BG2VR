namespace BG2VR.WorldUi
{
    /// <summary>ボタン帯のボタン種別。None はどのボタンにも hover していない。</summary>
    public enum PanelButtonKind { None = 0, Move = 1, Scale = 2, Curve = 3 }

    /// <summary>PanelAdjustState の 1 フレーム評価結果。</summary>
    public struct PanelAdjustResult
    {
        public PanelButtonKind Drag; // None=非ドラッグ / Move・Scale=このフレーム適用するドラッグ
        public bool JustEngaged;     // このフレーム engage（基準捕捉・平滑 Reset・m_placed 確定）
        public bool CurveToggled;    // 曲面トグル発火（rising 一回のみ）
    }

    /// <summary>
    /// UI 調整ボタンの純粋状態機械（UnityEngine / BepInEx 非依存・xUnit 可）。
    /// engage = trigger rising edge かつボタン hover（押下保持のまま後から指しても engage しない）。
    /// ドラッグ中は hover を外れても継続し、解放は trigger release / 無効化 / snapshot invalid のみ。
    /// rising 追跡は disable 中も継続する（再有効化フレームの偽 rising 防止・旧 PanelGrabState と同規約）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-adjust-buttons-design.md §6
    /// </summary>
    public sealed class PanelAdjustState
    {
        private PanelButtonKind m_drag;
        private bool m_prevTrigger;

        public PanelAdjustResult Update(bool enabled, bool valid, bool trigger, PanelButtonKind hover)
        {
            bool t = valid && trigger;
            bool rising = t && !m_prevTrigger;
            m_prevTrigger = t;

            var r = new PanelAdjustResult();
            if (!enabled || !valid)
            {
                m_drag = PanelButtonKind.None; // mid-drag の OFF / 切断 → 即解放
                return r;
            }

            if (m_drag != PanelButtonKind.None)
            {
                if (!t) m_drag = PanelButtonKind.None; // 解放は release のみ（hover 外れでは継続）
                else r.Drag = m_drag;
            }
            else if (rising)
            {
                if (hover == PanelButtonKind.Move || hover == PanelButtonKind.Scale)
                {
                    m_drag = hover;
                    r.Drag = hover;
                    r.JustEngaged = true;
                }
                else if (hover == PanelButtonKind.Curve)
                {
                    r.CurveToggled = true; // トグルは即時発火・ドラッグ状態に入らない
                }
            }
            return r;
        }

        public void Clear()
        {
            m_drag = PanelButtonKind.None;
            m_prevTrigger = false;
        }
    }
}
