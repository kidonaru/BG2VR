using UnityEngine;
using BG2VR.VrInput;
using BG2VR.Locomotion; // GripPoseSmoother（tau は locomotion と共有・spec §6）
using UnityVRMod.Core;

namespace BG2VR.WorldUi
{
    /// <summary>PanelAdjustRunner の 1 フレーム評価結果（ProjectorRunner / ProcessUi への引き渡し）。</summary>
    internal struct AdjustOutcome
    {
        public PanelButtonKind Hover;  // 凍結 ray のボタン hover（None あり）
        public Vector3 ButtonHitPoint; // hover ボタンの world 交点（レティクル用）
        public Vector3 ButtonHitNormal; // hover ボタンの面法線（world・レティクル向き用）
        public bool Dragging;          // Move/Scale ドラッグ中
        public bool MoveDragging;      // 移動ドラッグ中（ProjectorRunner の config 書き戻し分岐）
        public bool JustEngaged;       // engage フレーム（基準捕捉・m_placed 確定）
        public bool CurveToggled;      // 曲面トグル発火（m_placed 確定）
        public bool SuppressUi;        // ゲーム UI への hover/click/パルス注入を抑止
    }

    /// <summary>
    /// UI 調整ボタン（移動/拡大/曲面）の毎フレ統括。
    /// ProjectorRunner から VrPointerRunner.ComputeRay の直後・ProcessUi の前に呼ばれる。
    /// hover/engage 判定は凍結 ray（クリック精度）・ドラッグ計算は非凍結（凍結 0.3s の初動固着回避）。
    /// 移動 = 位置のみ手に追従（engage 時の手→root 相対**位置**保持）。向きは PlacementSolver の
    /// 自動制御（直立帯 + ドーム）＝手の回転はパネルを回さない（spec §3）。
    /// 拡大 = レーザーピッチ差の指数マッピング → Configs.WorldUiSize 書き戻し（F10 と単一情報源）。
    /// 曲面 = Configs.WorldUiCurved 反転のみ（メッシュ再生成は ProjectorRunner の config watch）。
    /// ドラッグ中は SaveOnConfigSet を抑止し release/Reset で復元 + Save()
    ///（毎フレ config 書き戻しの .cfg 毎フレ保存を回避・spec §4 必須要件）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-auto-orient-design.md §3 §4
    /// </summary>
    internal sealed class PanelAdjustRunner
    {
        private readonly PanelAdjustState m_state = new PanelAdjustState();
        private readonly GripPoseSmoother m_smoother = new GripPoseSmoother();
        private Vector3 m_relPos;     // 移動: 手→root 相対位置（手 local。回転は自動制御＝保持しない）
        private float m_engageSize;   // 拡大: engage 時サイズ
        private float m_engagePitch;  // 拡大: engage 時ピッチ(rad)
        private bool m_cfgSuppressed; // SaveOnConfigSet 抑止中（idempotent 解除用）

        /// <summary>snap はポインタ手の生 snapshot（masking 前）。frame は同フレームの ray 計算結果。
        /// eyeLocalPos は現フレームの eye rig-local 位置（自動向き・ボタン距離スケール用）。</summary>
        public AdjustOutcome Tick(in VrPointerRunner.PointerFrame frame, VrControllerSnapshot snap,
            VrUiPanel panel, Vector3 eyeLocalPos, float dt)
        {
            var outcome = new AdjustOutcome();
            try
            {
                return TickCore(frame, snap, panel, eyeLocalPos, dt, ref outcome);
            }
            finally
            {
                // SaveOnConfigSet 抑止はここで一元管理: ドラッグ継続が確定したフレームだけ抑止し、
                // それ以外（release / 無効化 / 切断 / 本体の例外離脱）では必ず復元 + 一括 Save する
                //（spec §4。途中 throw で復元漏れ→「以降の全 config 変更が保存されない」を構造的に排除）。
                // engage フレームの順序も成立する: Move の encode は Tick 後（ProjectorRunner）＝この
                // Begin が先行、Scale の初回書込は engage+1 フレーム＝前フレームの Begin で抑止済み。
                if (outcome.Dragging) BeginConfigBatch();
                else EndConfigBatch();
            }
        }

        private AdjustOutcome TickCore(in VrPointerRunner.PointerFrame frame, VrControllerSnapshot snap,
            VrUiPanel panel, Vector3 eyeLocalPos, float dt, ref AdjustOutcome outcome)
        {
            bool panelOk = panel != null && panel.Exists && panel.Buttons != null;
            bool enabled = panelOk && global::BG2VR.Configs.EnableUiAdjustButtons.Value;

            // hover（凍結 ray）と表示判定（拡張矩形・非表示中も判定＝フリッカ排除）
            bool expandedHit = false;
            if (enabled && frame.Active)
            {
                outcome.Hover = panel.Buttons.RaycastButtons(frame.Origin, frame.Dir,
                    out Vector3 hp, out Vector3 hn);
                outcome.ButtonHitPoint = hp;
                outcome.ButtonHitNormal = hn;
                expandedHit = ExpandedRectHit(frame.Origin, frame.Dir, panel);
            }

            var r = m_state.Update(enabled, frame.Active && snap.Valid, frame.Trigger.Pressed, outcome.Hover);

            if (r.CurveToggled)
            {
                outcome.CurveToggled = true;
                global::BG2VR.Configs.WorldUiCurved.Value = !global::BG2VR.Configs.WorldUiCurved.Value;
            }

            if (r.Drag == PanelButtonKind.Move)
            {
                // 手振れ平滑（engage 時 Reset＝平滑遅れ持ち越しによるジャンプ防止・locomotion と同じ）。
                if (r.JustEngaged) m_smoother.Reset();
                m_smoother.Update(
                    snap.RigLocalPosition, snap.RigLocalRotation,
                    dt, global::BG2VR.Configs.GrabMoveSmoothingTau.Value,
                    out Vector3 handPos, out Quaternion handRot);

                Transform rt = panel.RootTransform;
                if (rt != null)
                {
                    if (r.JustEngaged)
                    {
                        // engage 瞬間の「手→root」相対位置のみ捕捉（回転は自動制御なので保持しない）。
                        PanelGrabSolver.ToFrame(handPos, handRot, rt.localPosition, rt.localRotation,
                            out m_relPos, out _);
                    }
                    else
                    {
                        // 押し引き（移動ドラッグ中スティック上下・masking 前の生 Stick・spec §6）
                        m_relPos = PanelGrabSolver.PushPull(m_relPos, snap.Stick.y, dt);
                    }
                    // 位置 = 手追従（手の回転は relPos の腕として効く「レーザーポール」感のみ残る）。
                    Vector3 pos = handPos + handRot * m_relPos;
                    // 向き = 自動（常に視点側・直立帯 + ドーム・spec §3）。
                    Quaternion rot = PlacementSolver.ComputeRotation(
                        pos - eyeLocalPos, global::BG2VR.Configs.WorldUiUprightAngleDeg.Value);
                    panel.ApplyPose(pos, rot);
                    panel.UpdateEyeDistance(eyeLocalPos);
                }
                outcome.MoveDragging = true;
            }
            else if (r.Drag == PanelButtonKind.Scale)
            {
                // ドラッグ計算は非凍結 dir（凍結だと初動 VrFreezeTimeout 秒固まる）。
                float pitch = ScaleDragSolver.Pitch(frame.RawDir);
                if (r.JustEngaged)
                {
                    m_engageSize = global::BG2VR.Configs.WorldUiSize.Value;
                    m_engagePitch = pitch;
                }
                else
                {
                    // clamp range は WorldUiSize ConfigEntry を単一の真実源とする（yaml 変更へ自動追従）。
                    // AcceptableValues の静的型は AcceptableValueBase。range 付き Bind 済み＝実体は
                    // AcceptableValueRange<float> で非 null 確定（Configs に null ガード不要）。
                    var range = (global::BepInEx.Configuration.AcceptableValueRange<float>)
                        global::BG2VR.Configs.WorldUiSize.Description.AcceptableValues;
                    global::BG2VR.Configs.WorldUiSize.Value = ScaleDragSolver.Solve(
                        m_engageSize, m_engagePitch, pitch, range.MinValue, range.MaxValue);
                }
            }

            outcome.Dragging = r.Drag != PanelButtonKind.None;
            outcome.JustEngaged = r.JustEngaged;
            outcome.SuppressUi = outcome.Dragging || outcome.Hover != PanelButtonKind.None;

            // 表示・tint 反映（enabled OFF / frame 非 Active では常に消灯）
            bool barVisible = enabled && frame.Active && (expandedHit || outcome.Dragging);
            if (panelOk)
            {
                panel.Buttons.SetVisible(barVisible);
                if (barVisible)
                    panel.Buttons.SetTints(outcome.Hover, r.Drag, global::BG2VR.Configs.WorldUiCurved.Value);
            }
            return outcome;
        }

        // 拡張矩形（パネル+帯+余白・z=0 平面）への命中。表示判定専用。
        // normal はパネル本体（ProcessUi）・ボタンと同じく forward（QuadRaycaster は符号不感・plan-review 🔴1）。
        private static bool ExpandedRectHit(Vector3 origin, Vector3 dir, VrUiPanel panel)
        {
            Transform rt = panel.RootTransform;
            if (rt == null) return false;
            ButtonBarLayout.ExpandedRect(panel.Width, panel.Height, panel.Buttons.CurrentSide,
                out Vector3 localCenter, out float hw, out float hh);
            // 中心オフセット・半辺とも rig-local 値 → lossyScale で実寸化（WorldScale≠1 対応）。
            float s = rt.lossyScale.x;
            Vector3 center = rt.position + rt.rotation * (localCenter * s);
            var hit = QuadRaycaster.Raycast(origin, dir, center, rt.right, rt.up, rt.forward, hw * s, hh * s, 1, 1);
            return hit.Valid;
        }

        // ドラッグ中の SaveOnConfigSet 抑止。復元漏れ＝「以降の全 config 変更が保存されない」事故に
        // なるため、フラグで idempotent にし Reset（teardown 経路）でも必ず解除する（spec §4）。
        private void BeginConfigBatch()
        {
            if (m_cfgSuppressed) return;
            var cf = global::BG2VR.Configs.WorldUiSize.ConfigFile;
            if (cf == null) return;
            cf.SaveOnConfigSet = false;
            m_cfgSuppressed = true;
        }

        private void EndConfigBatch()
        {
            if (!m_cfgSuppressed) return;
            m_cfgSuppressed = false;
            var cf = global::BG2VR.Configs.WorldUiSize.ConfigFile;
            if (cf == null) return;
            cf.SaveOnConfigSet = true;
            cf.Save();
        }

        public void Reset()
        {
            m_state.Clear();
            m_smoother.Reset();
            EndConfigBatch(); // mid-drag teardown（Tick が呼ばれなくなる経路）でも必ず保存を復元
        }
    }
}
