using UnityEngine;
using UnityVRMod.Core;
using BG2VR.WorldUi;

namespace BG2VR.VrInput
{
    /// <summary>
    /// VR ポインタ入力の毎フレ統括。ProjectorRunner が ComputeRay → (PanelAdjustRunner) → ProcessUi の
    /// 順で呼ぶ（ボタンドラッグが ray と UI 注入の間に挟まるための分割・spec §7）。
    /// </summary>
    internal sealed class VrPointerRunner
    {
        /// <summary>1 フレームの ray 計算結果。Origin/Dir は凍結適用後（hover/click/レーザー用）、
        /// RawOrigin/RawDir は凍結適用前・平滑後（ドラッグ計算用）。</summary>
        public struct PointerFrame
        {
            public bool Active;
            public Vector3 Origin;
            public Vector3 Dir;
            public Vector3 RawOrigin;
            public Vector3 RawDir;
            public PointerButtonState.Edge Trigger;
        }

        /// <summary>前面オーバーレイ（設定パネル）の raycast 結果。Consumed=true でゲームUIを抑止しゲームレーザーを消す
        /// （設定パネルは前面 queue＝ゲームレーザーでは隠れるため視覚は設定側 VrLaserVisual に委ねる）。</summary>
        public struct FrontHit { public bool Consumed; public Vector3 Point; public Vector3 Normal; }

        private VrPointer m_pointer;
        private VrLaserVisual m_laser;
        private UiPointerDriver m_driver;
        private readonly PointerButtonState m_button = new PointerButtonState();
        private readonly RaySmoother m_smoother = new RaySmoother();
        private readonly PointerFreezeGate m_freezeGate = new PointerFreezeGate();
        private Vector3 m_frozenOrigin;
        private Vector3 m_frozenDir;
        private VrUiPanel m_panel;

        public void Setup(Transform rig, VrUiPanel panel)
        {
            m_panel = panel;
            m_pointer = new VrPointer();
            m_pointer.Create(rig);
            m_laser = new VrLaserVisual();
            m_laser.Create(m_pointer.Transform);
            m_driver = new UiPointerDriver();
        }

        /// <summary>
        /// ポインタ手切替フレームに呼ぶ（ProjectorRunner）。旧手の状態（平滑/凍結/トリガーエッジ/
        /// hover 押下）を全て捨てる。旧手のトリガーを押したまま切替えると新手の位置で偽の release
        /// エッジが発生し UI が誤クリック/固着するため、エッジ状態ごとリセットする（spec §4-5・Phase4）。
        /// </summary>
        public void OnPointerHandSwitched()
        {
            m_smoother.Reset();
            m_freezeGate.Reset();
            m_button.Reset();
            m_driver?.ClearHover();
        }

        // 呼び出し元 ProjectorRunner が m_active（=VRModCore.IsVrActive かつ projector built）を保証する
        // 定常状態でのみ呼ぶ。snap は呼び出し元が pointer 手で 1 回だけ読んだもの
        //（二重 native 読取・Trigger hysteresis 二重更新を避ける）。

        /// <summary>
        /// ray 計算（pointer pose 適用 → 平滑 → 凍結 latch）。旧 Tick の前半。
        /// suppressed=true（ポインタ手が grip 移動中）は非 Active を返す（レーザー消灯は ProcessUi 側）。
        /// </summary>
        public PointerFrame ComputeRay(VrControllerSnapshot snap, bool suppressed)
        {
            // 前フレームのクリックで誰も読まなかったパルスを失効させる（寿命 ≦ 1 フレームサイクル）。
            // あらゆる early-return より前＝panel 破棄直後等の経路でも必ず失効が走る。
            GbInputBridge.LeftClickPulse = false;

            if (m_pointer == null) return default;

            if (!global::BG2VR.Configs.EnableVrPointer.Value || suppressed || !snap.Valid)
            {
                m_button.Reset();
                m_smoother.Reset();
                m_freezeGate.Reset();
                return default;
            }

            // 毎フレ read ＝ F10 スライダーで live 反映
            m_pointer.Apply(snap, global::BG2VR.Configs.VrLaserPitchDeg.Value);
            Vector3 origin = m_pointer.Origin;
            Vector3 dir = m_pointer.Direction;

            // ② 手ブレ平滑（EMA・毎フレ config read ＝ F10 ライブ反映）。
            // 凍結中も更新し続け、解除時に現在の照準へ即復帰させる。
            // dt は unscaledDeltaTime: VR ポインタは頭/手トラッキング由来のリアルタイム入力＝ゲームの
            // timeScale に縛られない。ポーズメニュー(timeScale=0)中に deltaTime=0 だと alpha=1-e^0=0 で
            // 平滑出力が前フレ値に固着しレーザー/カーソルが凍結する（timeScale=1 では両者一致＝挙動不変）。
            m_smoother.Update(origin, dir, Time.unscaledDeltaTime,
                global::BG2VR.Configs.VrPointerSmoothingTau.Value, out origin, out dir);
            Vector3 rawOrigin = origin;
            Vector3 rawDir = dir;

            // ① トリガー押し込み開始（アナログ onset）でカーソル凍結（しきい値は F10 live）。
            // onset は fork click 閾値(0.7) より先に超えるため、click 確定時点で ray は狙った位置で
            // 凍結済み。レーザー/レティクル/raycast 全部 latch 値＝表示と判定が一致。
            // 解除後は VrFreezeRecover 秒の smoothstep で現照準へブレンド復帰（ワープしない）。
            // dt は unscaledDeltaTime（上の平滑と同理由）。timeScale=0 だと復帰タイマーが進まず、
            // ポーズ中にクリックするとカーソルが凍結位置に固着し続ける（freeze recover 不達）。
            var freeze = m_freezeGate.Update(
                snap.TriggerValue, Time.unscaledDeltaTime,
                global::BG2VR.Configs.VrFreezeOnset.Value,
                global::BG2VR.Configs.VrFreezeRelease.Value,
                global::BG2VR.Configs.VrFreezeTimeout.Value,
                global::BG2VR.Configs.VrFreezeRecover.Value);
            if (freeze.JustFroze)
            {
                m_frozenOrigin = origin;
                m_frozenDir = dir;
            }
            if (freeze.Blend < 1f)
            {
                origin = Vector3.Lerp(m_frozenOrigin, origin, freeze.Blend);
                Vector3 bd = Vector3.Lerp(m_frozenDir, dir, freeze.Blend);
                dir = bd.sqrMagnitude > 1e-12f ? bd.normalized : dir;
            }

            return new PointerFrame
            {
                Active = true,
                Origin = origin,
                Dir = dir,
                RawOrigin = rawOrigin,
                RawDir = rawDir,
                Trigger = m_button.Update(snap.Trigger),
            };
        }

        /// <summary>直近 ProcessUi のパネル命中（パネル操作系の実機切り分け・診断用）。</summary>
        public bool LastHitValid { get; private set; }

        /// <summary>depth test 設定の live 反映（ProjectorRunner watch → レーザー/レティクルへ passthrough。
        /// 線とレティクルで実効値が分かれるため laser 側で種別ごとに解決する）。</summary>
        public void ApplyDepthTest() => m_laser?.ApplyDepthTest();

        /// <summary>
        /// パネル raycast（平面/曲面で raycaster 切替）+ レーザー表示 + ゲーム UI への hover/click 注入。
        /// 旧 Tick の後半。adjust.SuppressUi=true（帯 hover/ドラッグ中）は注入と LeftClickPulse を抑止し、
        /// レーザー終端をボタン交点に置く。
        /// </summary>
        public void ProcessUi(in PointerFrame frame, in AdjustOutcome adjust, in FrontHit front)
        {
            if (m_driver == null) return;

            // front.Consumed=前面(設定パネル)命中＝ゲームUIは抑止しゲームレーザーを消灯（設定側レーザーが点灯）。
            // 設定パネルは前面 queue(SettingsOverlayQueue)＝ゲームレーザー(LaserQueue)では隠れるため視覚は設定側へ委ねる。
            // !frame.Active / panel 不在も同じく「ゲームUI何もしない + レーザー消灯」へ集約。
            if (!frame.Active || front.Consumed || m_panel == null || !m_panel.Exists)
            {
                LastHitValid = false;
                m_laser?.UpdateVisual(false, Vector3.zero, Vector3.forward, false, Vector3.zero, Vector3.zero);
                m_driver.ClearHover();
                return;
            }

            Transform rt = m_panel.RootTransform;
            QuadRaycaster.Hit hit = default;
            if (rt != null)
            {
                // rig は localScale=1/WorldScale を持ち、パネル実描画寸法は rig-local 寸法 × lossyScale。
                // raycaster には実寸を渡す（未スケールだと WorldScale≠1 で端ほどクリックがずれる）。
                float s = rt.lossyScale.x;
                // normal はボタン/拡張矩形と同じく forward（QuadRaycaster は符号不感・plan-review 🔴1）。
                hit = m_panel.Curved
                    ? CurvedPanelRaycaster.Raycast(
                        frame.Origin, frame.Dir, rt.position, rt.rotation,
                        m_panel.Width * s, m_panel.Height * s, m_panel.Radius * s,
                        VrUiCompositor.RtWidth, VrUiCompositor.RtHeight)
                    : QuadRaycaster.Raycast(
                        frame.Origin, frame.Dir, rt.position, rt.right, rt.up, rt.forward,
                        m_panel.Width * 0.5f * s, m_panel.Height * 0.5f * s,
                        VrUiCompositor.RtWidth, VrUiCompositor.RtHeight);
            }
            LastHitValid = hit.Valid;

            // レーザー終端: ボタン hover 中はボタン交点（レティクルがボタンに乗る）。
            bool buttonHit = adjust.Hover != PanelButtonKind.None;
            bool laserHit = hit.Valid || buttonHit;
            Vector3 laserPoint = buttonHit ? adjust.ButtonHitPoint : hit.WorldPoint;
            Vector3 laserNormal = buttonHit ? adjust.ButtonHitNormal : hit.Normal;
            m_laser.UpdateVisual(global::BG2VR.Configs.VrLaserVisible.Value,
                frame.Origin, frame.Dir, laserHit, laserPoint, laserNormal);

            if (adjust.SuppressUi)
            {
                m_driver.ClearHover(); // 押下保持中なら pointerUp（キャンセル）＝固着なし
                return;
            }

            bool consumedByUi = m_driver.Process(hit.Valid, hit.Pixel, frame.Trigger);

            // 非インタラクティブ領域（会話メッセージ部・会計伝票など）のトリガークリック＝
            // 「実マウス左クリック 1 回」としてゲームの LeftClick ポーリング待ちへ橋渡し。
            // 消費は read=consume（GbInputBridge 参照）、未読なら次 ComputeRay 冒頭で失効。
            if (hit.Valid && frame.Trigger.JustPressed && !consumedByUi)
                GbInputBridge.LeftClickPulse = true;
        }

        public void Teardown()
        {
            // teardown 後は ComputeRay が止まり失効処理が走らないため、未読パルスをここで捨てる。
            GbInputBridge.LeftClickPulse = false;
            LastHitValid = false;
            m_driver?.ClearHover();
            if (m_laser != null) { m_laser.Destroy(); m_laser = null; }
            if (m_pointer != null) { m_pointer.Destroy(); m_pointer = null; }
            m_driver = null;
            m_panel = null;
            m_button.Reset();
            m_smoother.Reset();
            m_freezeGate.Reset();
        }
    }
}
