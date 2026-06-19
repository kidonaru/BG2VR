using UnityEngine;
using UnityVRMod.Core;
using BG2VR.VrInput;
using BG2VR.Patches.Settings;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// 設定パネル(F10・UI Toolkit)を VR HMD 内に表示・操作する統括（ProjectorRunner が所有・毎フレ Tick）。
    /// ①開閉コンボ（両 stick-click hold）で Show/Hide ②表示中は PanelSettings.targetTexture を RT へ
    /// リダイレクト→VrSettingsPanel(world quad)に UV クロップ表示 ③VR ボタン → SettingsView 既存ナビ handler。
    /// 入力アービトレーション（§6.5）は ProjectorRunner の責務＝本 Tick の戻り値 ModalActive 中はゲーム入力を skip。
    /// RT 解放順は ChekiCameraRunner 踏襲（targetTexture=null → Release → Destroy）。
    /// </summary>
    internal sealed class SettingsPanelRunner
    {
        private const int RtWidth = 1920;
        private const int RtHeight = 1080;

        private readonly SettingsPanelInput m_input = new SettingsPanelInput();
        private VrSettingsPanel m_panel;
        private RenderTexture m_rt;
        private Transform m_boundRig;

        // 設定パネルの前面レーザー視覚（ゲーム UI レーザーとは別実体・queue SettingsLaserQueue＝設定パネル前面）。
        // 入力 ray はゲームの統一 ray（ProcessLaserShared 引数）を流用＝独自 pitch/平滑/arbiter/ボタンは持たない。
        private BG2VR.VrInput.VrLaserVisual m_laserVis;
        private Transform m_laserRig;

        public struct TickResult
        {
            public bool PanelShown;         // 設定パネル表示中＝ProjectorRunner が前面 raycast の gate に使う（非モーダル）
            public bool ConsumeStickClick;  // 開閉コンボ hold 中＝game Auto へ stick-click を渡さない
        }

        public TickResult Tick(Transform rig, VrUiPanel gamePanel, VrControllerSnapshot left, VrControllerSnapshot right,
            Vector3 eyeLocal, float dt)
        {
            var result = new TickResult();
            var ctl = SettingsController.Instance;
            if (ctl == null || ctl.View == null)
            {
                TeardownVisual(null);
                return result;
            }
            var view = ctl.View;
            bool shown = view.IsShown;

            var inputs = new SettingsPanelInput.Inputs
            {
                LeftValid = left.Valid,
                RightValid = right.Valid,
                RightStick = right.Valid ? right.Stick : Vector2.zero,
                LeftStickClick = left.Valid && left.StickClick,
                RightStickClick = right.Valid && right.StickClick,
                LeftA = left.Valid && left.A,
                LeftB = left.Valid && left.B,
                RightA = right.Valid && right.A,
                RightB = right.Valid && right.B,
                RightTrigger = right.Valid && right.Trigger,
                PanelShown = shown,
            };
            var act = m_input.Update(inputs,
                global::BG2VR.Configs.VrNavStickThreshold.Value,
                global::BG2VR.Configs.SettingsNavRepeatDelay.Value,
                global::BG2VR.Configs.SettingsNavRepeatInterval.Value,
                global::BG2VR.Configs.SettingsToggleHoldSec.Value,
                dt);
            result.ConsumeStickClick = act.ConsumeStickClick;

            // 開閉トグル（両 stick-click hold コンボ）。閉じるも同コンボ＝離脱境界が ConsumeStickClick で安全
            //（右 B 閉じは撤去・モーダル離脱フレームでゲームの「戻る」誤発火を招くため）。
            if (act.ToggleOpen) { if (shown) view.Hide(); else view.Show(); shown = view.IsShown; }

            if (!shown || rig == null)
            {
                TeardownVisual(view); // RT redirect off + quad/RT 破棄
                return result;        // PanelShown=false
            }

            // ── 表示: RT 確保 → redirect → quad → 合成配置 ──
            EnsureRt();
            view.SetTargetTexture(m_rt);
            EnsurePanel(rig);
            ApplyComposite(gamePanel, eyeLocal);
            // 非モーダル化（2026-06-14）でボタンナビは撤去＝設定値はレーザーのみで変更（#2 誤動作防止）。
            // SettingsPanelInput.Update の nav 計算（RowUp/Slide/Confirm/Tab）は dead だが開閉コンボ判定のため
            // Update 自体は残す（既存純関数テスト維持・nav 計算コードは温存）。レーザーは ProjectorRunner が
            // 統一 ray で ProcessLaserShared を呼ぶ（前面優先・外側はゲームUI）。

            result.PanelShown = true;
            return result;
        }

        // ゲーム UI パネル(VrUiPanel)生存時はその pose/サイズ/曲面を継承して重ねる＝デスクトップ一致の合成
        // （前面化は VrSettingsPanel の SettingsOverlayQueue + depthTest=false が担う＝手前オフセット不要）。
        // 不在/!Exists 時（タイトル等 canvas 不在・遷移直後）は既定配置で単独表示（§6 仕様確定）。
        private void ApplyComposite(VrUiPanel gamePanel, Vector3 eyeLocal)
        {
            // SetCurved/ApplyScale は順序非依存（各 setter が現在の他フィールドを読んで RebuildMesh する。
            // 幅未確定の初フレームは SetCurved の rebuild が no-op ガードに落ち、後続 ApplyScale が正しい幅で確定する）。
            if (gamePanel != null && gamePanel.Exists)
            {
                Transform gt = gamePanel.RootTransform;
                m_panel.SetCurved(gamePanel.Curved, gamePanel.Radius); // 曲面は継承（重ね合成の見た目一致）
                // 幅は独立（SettingsPanelSize）＝ゲームUIより小さく重ねる＝外周はゲームUI(option B)。
                m_panel.ApplyScale(global::BG2VR.Configs.SettingsPanelSize.Value);
                if (gt != null) m_panel.ApplyPose(gt.localPosition, gt.localRotation); // 位置/向きは継承（重ねる）
            }
            else
            {
                // 既定配置: 設定パネルは正面・目線高さ固定（VerticalOffset/Yaw config は持たず距離とサイズのみ調整可。
                // ゲーム UI パネル既定〔WorldUiVerticalOffset/Yaw 使用〕とは意図的に非対称）。
                Vector3 offset = PlacementSolver.Decode(global::BG2VR.Configs.SettingsPanelDistance.Value, 0f, 0f);
                Quaternion rot = PlacementSolver.ComputeRotation(offset, global::BG2VR.Configs.WorldUiUprightAngleDeg.Value);
                m_panel.SetCurved(false, 1f);
                m_panel.ApplyScale(global::BG2VR.Configs.SettingsPanelSize.Value);
                m_panel.ApplyPose(eyeLocal + offset, rot);
            }
        }

        private void EnsureRt()
        {
            if (m_rt != null) return;
            m_rt = new RenderTexture(RtWidth, RtHeight, 0, RenderTextureFormat.ARGB32);
            m_rt.name = "BG2VR_SettingsRT";
        }

        private void EnsurePanel(Transform rig)
        {
            if (m_panel != null && m_panel.Exists && m_boundRig == rig) return;
            // rig 差替え or 未生成 → 作り直し（quad は rig 子＝rig 破棄で道連れになるため再生成）。
            if (m_panel != null) m_panel.Destroy();
            m_panel = new VrSettingsPanel();
            m_panel.Create(rig, m_rt);
            m_boundRig = rig;
        }

        /// <summary>表示終了/VR 非アクティブ時の視覚クリーンアップ（screen 復元 + quad/RT 破棄）。</summary>
        private void TeardownVisual(SettingsView view)
        {
            if (view != null) view.SetTargetTexture(null); // screen 描画へ復元（desktop F10 非回帰）
            TeardownLaser(); // モーダル終了＝レーザー実体も破棄（quad/RT と同じライフサイクル）
            if (m_panel != null) { m_panel.Destroy(); m_panel = null; }
            m_boundRig = null;
            if (m_rt != null) { m_rt.Release(); Object.Destroy(m_rt); m_rt = null; }
        }

        /// <summary>非モーダル前面レーザー。ProjectorRunner が統一 ray（ゲーム ComputeRay の pitch+平滑済み
        /// origin/dir）と trigger エッジを渡す。設定パネルを raycast→view.HandleLaser へ橋渡しし、命中時のみ
        /// 専用レーザー(前面 queue)を点灯して FrontHit.Consumed=true を返す（呼出側が ProcessUi へ渡しゲームUIを抑止）。
        /// 非命中/不可時は消灯し view.LaserEnd で設定 hover を解く。独自 pitch/平滑/arbiter/ボタンは持たない
        /// （ray・手選択・エッジはゲームと共有＝レーザーは常に 1 本・線は同一 ray で継ぎ目なし）。</summary>
        public VrPointerRunner.FrontHit ProcessLaserShared(Transform rig, Vector3 origin, Vector3 dir,
            bool triggerHeld, bool justPressed, bool justReleased)
        {
            var none = default(VrPointerRunner.FrontHit);
            if (!global::BG2VR.Configs.EnableVrPointer.Value) { HideLaser(); return none; }
            var ctl = SettingsController.Instance;
            var view = ctl != null ? ctl.View : null;
            if (view == null || m_panel == null || !m_panel.Exists) { HideLaser(); return none; }

            EnsureLaser(rig);

            QuadRaycaster.Hit hit = default;
            Transform rt = m_panel.RootTransform;
            if (rt != null)
            {
                float s = rt.lossyScale.x; // rig localScale=1/WorldScale を実寸へ戻す（ゲーム UI raycast と同契約）
                hit = m_panel.Curved
                    ? CurvedPanelRaycaster.Raycast(origin, dir, rt.position, rt.rotation,
                        m_panel.Width * s, m_panel.Height * s, m_panel.Radius * s, RtWidth, RtHeight)
                    : QuadRaycaster.Raycast(origin, dir, rt.position, rt.right, rt.up, rt.forward,
                        m_panel.Width * 0.5f * s, m_panel.Height * 0.5f * s, RtWidth, RtHeight);
            }

            Vector2 uv = hit.Valid
                ? new Vector2(hit.Pixel.x / RtWidth, hit.Pixel.y / RtHeight)
                : Vector2.zero;
            // consumed = 設定ウィンドウ(m_root)上を指した（or ドラッグ中）か。透明マージン(panel 余白)は false ＝
            // ゲームUIへ通す。幾何 quad ヒット(hit.Valid)ではなく「実要素を pick したか」で前面消費を決める
            // （quad 全面が透明 RT＝幾何ヒットで判定を吸う問題の修正・2026-06-14）。
            bool consumed = view.HandleLaser(hit.Valid, uv, justPressed, triggerHeld, justReleased);

            // 専用レーザー視覚: 設定ウィンドウ上(consumed)のみ点灯。マージンはゲームレーザーへ譲る（線は統一 ray と同一）。
            // レティクル(第4引数)は幾何 quad ヒット(hit.Valid)で判定する＝consumed ではない（ドラッグ中は quad 外でも
            // consumed=true だが hit.Valid=false＝WorldPoint 無効。consumed を渡すと原点(0,0,0)へレティクルが飛ぶ）。
            // → ドラッグ中 quad 外は「点灯+miss 線・レティクル消灯」で旧挙動に一致。
            m_laserVis.UpdateVisual(consumed && global::BG2VR.Configs.VrLaserVisible.Value,
                origin, dir, hit.Valid, hit.WorldPoint, hit.Normal);

            return consumed
                ? new VrPointerRunner.FrontHit { Consumed = true, Point = hit.WorldPoint, Normal = hit.Normal }
                : none;
        }

        // 専用レーザー視覚のみ確保（独自 VrPointer は持たず rig 直下に視覚だけ生成）。rig 差替えで作り直し。
        private void EnsureLaser(Transform rig)
        {
            if (m_laserVis != null && m_laserRig == rig) return;
            TeardownLaser();
            m_laserVis = new BG2VR.VrInput.VrLaserVisual();
            m_laserVis.Create(rig, UiOverlayRenderPolicy.SettingsLaserQueue);
            m_laserRig = rig;
        }

        // レーザーを消し設定 hover を解く（毎フレ非命中で呼ばれる＝冪等）。視覚実体は保持（次フレ再点灯が軽い）。
        private void HideLaser()
        {
            m_laserVis?.UpdateVisual(false, Vector3.zero, Vector3.forward, false, Vector3.zero, Vector3.zero);
            SettingsController.Instance?.View?.LaserEnd();
        }

        /// <summary>前面非対象フレーム（設定非表示/ray 無効/SuppressUi）に ProjectorRunner から呼び、設定レーザーを消灯し hover を解く。</summary>
        public void HideLaserExternal() => HideLaser();

        /// <summary>左スティック縦入力を設定パネルのホイールスクロールへ橋渡しする
        /// （ProjectorRunner が PanelShown 時に毎フレ呼ぶ）。stickY: -1..1（上が正）。
        /// deadzone はドリフト防止の構造値（VrNavStickThreshold=離散ナビ閾値とは別・アナログ効き始め用）。</summary>
        public void ScrollByStick(float stickY, float speed, float dt)
        {
            var view = SettingsController.Instance?.View;
            if (view == null || !view.IsShown) return;
            const float deadzone = 0.15f;
            float delta = BG2VR.VrInput.SettingsScrollMath.Delta(stickY, deadzone, speed, dt);
            if (delta != 0f) view.ScrollByDelta(delta);
        }

        // レーザー視覚を破棄（表示終了 / VR 非アクティブ / rig 差替え）。
        private void TeardownLaser()
        {
            if (m_laserVis != null) { m_laserVis.Destroy(); m_laserVis = null; }
            m_laserRig = null;
            SettingsController.Instance?.View?.LaserEnd();
        }

        /// <summary>VR 非アクティブ/OnDestroy 時の完全 teardown（ProjectorRunner から）。</summary>
        public void Teardown()
        {
            var ctl = SettingsController.Instance;
            TeardownVisual(ctl != null ? ctl.View : null);
            m_input.Reset();
        }
    }
}
