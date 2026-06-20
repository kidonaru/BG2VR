using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityVRMod.Core; // VRModCore（namespace は UnityVRMod.Core）
using BG2VR.VrInput;   // VrPointerRunner

namespace BG2VR.WorldUi
{
    /// <summary>
    /// WorldUiProjector のライフサイクル統括（MonoBehaviour）。
    /// VR active 化で構築、inactive/遷移 teardown で破棄、rig 変化で再構築、
    /// scene 変化で root 再解決、hotkey で再センター、config 変更で再配置。
    /// </summary>
    internal sealed class ProjectorRunner : MonoBehaviour
    {
        private VrUiCompositor m_compositor;
        private VrUiPanel m_panel;
        private Transform m_boundRig;
        private bool m_active;
        private bool m_sceneDirty;
        private VrPointerRunner m_pointerRunner;

        // 配置フレームの原点（eye の rig-local 位置）。配置確定/再センター/未配置フェーズの基準。
        // 回転は常に rig 軸（identity）＝配置は頭の向きに一切依存しない（spec §5・頭追従廃止）。
        private Vector3 m_frameOrigin = Vector3.zero;

        // 配置は config 3 スカラー（WorldUiDistance/WorldUiVerticalOffset/WorldUiYaw・rig 軸相対）が
        // 単一情報源（spec §4・手動アンカー機構は廃止）。移動ドラッグ＝config 書き戻しのため
        // 遷移・ゲーム再起動をまたいで配置が復元される。フレーム回転を eye-yaw にしない理由:
        // 未配置フェーズの毎フレ decode で頭の向きに連動して回り続け、「視線正面」として記録され
        // 配置意図を失う（実機 NG 2026-06-05）。rig 軸は全シーンで「プレイヤー正面」に正規化されている。

        // config live 反映用の前回値キャッシュ。
        private float m_lastDistance, m_lastSize, m_lastOffset, m_lastYaw, m_lastUprightDeg;
        private bool m_lastCurved;
        private float m_lastCurveRadius;
        private float m_lastButtonRatio, m_lastButtonOffset;
        private bool m_lastDepthTest;
        private bool m_lastOccludeUi;

        // パネルは未配置のうちは頭向きに追従し、最初の操作（トリガー click or 上ボタン）で world 固定する。
        private bool m_placed;

        // ポインタ手の自動切替（最後にトリガー onset に触れた手・初期=右）と VR ボタン割当。
        // arbiter は Teardown で意図的にリセットしない＝ポインタ手は遷移をまたいで持続する。
        private readonly BG2VR.VrInput.PointerHandArbiter m_handArbiter = new BG2VR.VrInput.PointerHandArbiter();
        private readonly BG2VR.VrInput.VrGameButtonsRunner m_buttonsRunner = new BG2VR.VrInput.VrGameButtonsRunner();
        private readonly BG2VR.Locomotion.GrabMoveRunner m_gripRunner = new BG2VR.Locomotion.GrabMoveRunner();
        private readonly PanelAdjustRunner m_adjustRunner = new PanelAdjustRunner();
        private readonly BG2VR.VrInput.ControllerModelRunner m_controllerModelRunner = new BG2VR.VrInput.ControllerModelRunner();
        private readonly BG2VR.VrInput.ChekiCameraRunner m_chekiCameraRunner = new BG2VR.VrInput.ChekiCameraRunner();
        private readonly BG2VR.DrinkGlass.DrinkGlassRunner m_drinkGlassRunner = new BG2VR.DrinkGlass.DrinkGlassRunner();
        private readonly BG2VR.KaraokeShake.KaraokeShakeRunner m_karaokeShakeRunner = new BG2VR.KaraokeShake.KaraokeShakeRunner();
        private readonly BG2VR.HandSumoPush.HandSumoPushRunner m_handSumoPushRunner = new BG2VR.HandSumoPush.HandSumoPushRunner();
        // 手元モデルの per-hand 種別（grip+トリガーで切替・**Teardown で消さない**＝遷移をまたいで保持）と
        // 切替コンボ検出。selector は既定 両手 Controller（起動時両手コントローラ＝レーザー即使用可）。
        private readonly HandModelSelector m_modelSelector = new HandModelSelector();
        private readonly ModelSwitchInput m_modelSwitch = new ModelSwitchInput();
        // 設定パネル(F10)の VR 表示・操作（ゲーム UI projector とは独立の別 quad・別 RT）。
        // 表示中はモーダル＝ゲーム入力 runner 群を skip し入力を設定パネルへ専有させる（§6.5）。
        private readonly SettingsPanelRunner m_settingsRunner = new SettingsPanelRunner();
        // 前フレームのドラッグ状態（arbiter の trigger masking 用。adjust は ray 計算後に走るため
        // 当該フレーム値はまだ無い＝1 フレーム遅れで使う。旧 grab の hoverValid と同じ前例・実害なし）。
        private bool m_adjustWasDragging;

        // 捕捉漏れ canvas の watchdog 周期（フレーム）。内部実装値＝config 化しない。
        private const int CanvasSweepIntervalFrames = 30;
        private int m_canvasSweepCounter;

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnActiveSceneChanged(Scene a, Scene b) => m_sceneDirty = true;
        private void OnSceneLoaded(Scene s, LoadSceneMode m) => m_sceneDirty = true;

        private void Update()
        {
            // 前フレームの注入パルスを失効させる（あらゆる早期 return より前＝teardown/再構築/VR 非 active の
            // 経路でも必ず失効が走る）。定常状態では後段の m_buttonsRunner.Tick が今フレーム分を上書きする。
            // これが無いと「Tick が呼ばれないフレーム」にパルスが残灯し、ゲーム読者が同じパルスを
            // 2 周期読む（VrPointerRunner.Tick 冒頭の LeftClickPulse 失効と同じ規約・plan-review 指摘）。
            BG2VR.VrInput.GbGameButtonBridge.Clear();
            // Cheki 視点固定(FreezeAim)も同様に毎フレ失効させる（非 VR/早期 return 経路でも CameraControll が
            // 元に戻る保証。撮影中は後段の ChekiCameraRunner.Tick が当該フレーム値を再セット）。
            BG2VR.VrInput.ChekiInputBridge.ResetFrame();

            bool want = Configs.WorldUiEnabled.Value && VRModCore.IsVrActive;
            Transform rig = want ? VRModCore.GetRigTransform() : null;

            if (!want || rig == null)
            {
                m_controllerModelRunner.HideAll(); // VR 非 active 中の残像防止（rig 存命でも非表示）
                m_drinkGlassRunner.HideAll();      // ドリンクグラスも同様に残像防止
                // RT redirect を外し screen 描画へ復元するのは VR が真に非アクティブのときだけ（desktop F10 非回帰）。
                // VR active のまま rig が一時 null（遷移中の rebind）では redirect/RT を維持＝desktop へのちらつきと
                // RT 再確保を避ける（quad は rig 子で道連れ破棄 → rig 復帰後の Tick で再生成・code-review 🟢）。
                if (!VRModCore.IsVrActive) m_settingsRunner.Teardown();
                if (m_active) Teardown();
                return;
            }

            // 両手 snapshot を毎フレ読取（ボタン割当は左右対称・ポインタ手は arbiter が決定）。
            // 設定パネルモーダルより前に読むのは、モーダル評価がこの snapshot を要するため（同フレーム値・再読取しない）。
            VrControllerSnapshot left = VRModCore.GetControllerSnapshot(VrHand.Left);
            VrControllerSnapshot right = VRModCore.GetControllerSnapshot(VrHand.Right);

            // ── 設定パネル(F10)モーダルを最優先で評価（ゲーム UI projector のライフサイクルから独立） ──
            // m_panel（ゲーム UI パネル）を渡して重ね合成（生存時は pose/サイズ/曲面を継承）。
            // Tick はゲームパネル再構築(Setup・後段)より前＝m_panel は前フレーム状態（定常では有効・§6 仕様）。
            var settings = m_settingsRunner.Tick(rig, m_panel, left, right, CurrentEyeLocal(), Time.deltaTime);
            // 設定パネルは非モーダル前面オーバーレイ（2026-06-14・ユーザー決定=完全非モーダル）＝表示中もゲーム入力/
            // モデル種別 override/locomotion は通常どおり走る（#1 手モデル維持はこれで自動成立）。設定値はレーザー
            // のみで変更（#2・前面 raycast は ProcessUi 直前で実施・命中時のみゲームUI抑止）。外側はゲームUIへ通す（#3）。
            // 開閉コンボ hold 中は stick-click を消費（single=Auto の二重発火防止・§6.5）。
            if (settings.ConsumeStickClick) { left.StickClick = false; right.StickClick = false; }

            // 捕捉漏れ canvas の watchdog: 捕捉済み canvas は ScreenSpaceCamera 化済みのため、
            // 定常状態では Resolve()（Overlay のみ対象）は空が不変条件。非空 = シーン変化を伴わず
            // 後から active 化した root canvas が出現（エクストラ発ミニゲーム exit 後に再表示される
            // メニュー canvas が取り残され VR 非表示になった実バグ 2026-06-07）→ 再構築に乗せる。
            if (m_active && !m_sceneDirty && ++m_canvasSweepCounter >= CanvasSweepIntervalFrames)
            {
                m_canvasSweepCounter = 0;
                int lateCount = CanvasRootResolver.Resolve().Count;
                if (lateCount > 0)
                {
                    // 発火は稀イベント（後出し canvas 検知時のみ）＝ログノイズにならない。
                    // scene 変化由来の再構築との切り分け用（code-review 指摘）。
                    Plugin.Log.LogInfo($"[WorldUi] 後出し active 化した canvas {lateCount} 枚を検知 → 再捕捉のため再構築。");
                    m_sceneDirty = true;
                }
            }

            // rig が差し替わった / panel が道連れ破棄された / scene が変わった → 再構築
            if (!m_active || m_boundRig != rig || (m_panel != null && !m_panel.Exists) || m_sceneDirty)
            {
                Teardown();
                // UI 未生成で Setup が失敗したら m_sceneDirty を消さない（新シーン Canvas を
                // 取りこぼさないため。成功するまで毎フレーム再試行＝!m_active でも回る）。
                if (Setup(rig)) m_sceneDirty = false;
                return;
            }

            // カラオケ中の振り入力（ゲート/Reset は runner 内。bridge pulse は Update 先頭 Clear で失効済み）。
            // left/right は上の早期 snapshot 読取を再利用（設定モーダル評価と同フレーム値）。
            m_karaokeShakeRunner.Tick(left, right, Time.deltaTime);
            // 手押し相撲 PLAYING 中の両手押し出し入力（ゲート/Reset は runner 内）。
            m_handSumoPushRunner.Tick(left, right, Time.deltaTime);

            // raw grip（locomotion トグルから独立）: ポインタ抑制・arbiter ゼロ化・スティックゼロ化・コンボ判定に使う。
            bool gripL = left.Valid && left.Grip;
            bool gripR = right.Valid && right.Grip;

            // 頭の水平 forward/right（grip+スティック平行移動の基準）。eye 不在は zero ベクトル＝移動なし。
            Vector3 headFwd = Vector3.zero, headRight = Vector3.zero;
            Camera eyeCam = VRModCore.GetVrEyeCamera();
            if (eyeCam != null)
            {
                Vector3 f = eyeCam.transform.forward; f.y = 0f;
                Vector3 rt = eyeCam.transform.right; rt.y = 0f;
                if (f.sqrMagnitude > 1e-6f) headFwd = f.normalized;
                if (rt.sqrMagnitude > 1e-6f) headRight = rt.normalized;
            }

            // Grip locomotion（掴み移動 + スティック平行移動）: raw stick を読むため後段のスティックゼロ化より前に呼ぶ。
            // rig transform を以降の処理より先に確定させる（panel/レーザーは rig 子で追従）。
            m_gripRunner.Tick(rig, left, right, headFwd, headRight, Time.deltaTime);

            // モデル切替コンボ: grip 保持中のトリガー rising で per-hand に種別を回す（モデル Tick の前＝同フレーム反映）。
            m_modelSwitch.Update(gripL, left.Valid && left.Trigger, gripR, right.Valid && right.Trigger,
                out bool cycleL, out bool cycleR);

            // 今のミニゲーム → per-hand コンテキスト（gate + MiniGameProbe・優先順は従来の override と同一）。
            // selector がコンテキスト別ループ順・突入リセット・通常選択の永続を一括管理する（Resolve に内包）。
            // gate OFF のミニゲームは Normal 扱い＝従来の override スキップと等価（プロップ非表示時は通常ループ）。
            HandModelContext leftCtx = HandModelContext.Normal;
            HandModelContext rightCtx = HandModelContext.Normal;
            bool ahhnActive = false;
            if (Configs.ShowChekiCamera.Value && BG2VR.UiSceneVoid.MiniGameProbe.IsCheki())
            {
                // Cheki: 利き手をカメラ起点のループへ。反対手は Normal（従来どおりレーザー UI 操作可）。
                if (Configs.ChekiCameraRightHand.Value) rightCtx = HandModelContext.Cheki;
                else                                    leftCtx  = HandModelContext.Cheki;
            }
            else if (Configs.ShowKaraokeProps.Value && BG2VR.UiSceneVoid.MiniGameProbe.IsKaraoke())
            {
                // カラオケ: 両手をプロップ起点のループへ（左タンバリン/右サイリウム）。
                leftCtx = HandModelContext.Karaoke;
                rightCtx = HandModelContext.Karaoke;
            }
            else if (Configs.ShowHandSumoHands.Value && BG2VR.UiSceneVoid.MiniGameProbe.IsHandSumo())
            {
                // 手押し相撲: 両手をハンド起点のループへ。
                leftCtx = HandModelContext.HandSumo;
                rightCtx = HandModelContext.HandSumo;
            }
            else if (Configs.AhhnVrEnabled.Value && BG2VR.UiSceneVoid.MiniGameProbe.IsAhhnForCast())
            {
                // あ〜ん(食べさせ): 右手をハンド起点のループへ。左手は Normal（レーザー UI 操作を残す）。
                // 食べさせ判定の右トリガーは AhhnVrPatches が VRModCore 生 snapshot を直読み＝本コンテキスト非依存。
                rightCtx = HandModelContext.Ahhn;
                ahhnActive = true;
            }

            // ── ドリンク: NPC が手にグラス/カクテルを持つ間、ミニゲーム override の無い設定手を Drinking へ。
            // 突入で Hand・grip+トリガーで Controller に戻せる（カラオケ/Cheki と同挙動）。複製元 SMR は後段の
            // glass runner で使うため locals に退避（probe は env のキャラ走査＝軽量）。
            SkinnedMeshRenderer drinkSource = null;
            GB.Scene.CharacterHandle.Props drinkProp = default;
            if (Configs.ShowDrinkGlass.Value
                && BG2VR.DrinkGlass.DrinkGlassProbe.TryGetSource(out drinkSource, out drinkProp))
            {
                if (Configs.DrinkGlassLeftHand.Value)
                {
                    if (leftCtx == HandModelContext.Normal) leftCtx = HandModelContext.Drinking;
                }
                else if (rightCtx == HandModelContext.Normal) rightCtx = HandModelContext.Drinking;
            }

            // per-hand 種別を解決（reset → cycle → get を内包）。レーザー gating（コントローラ手のみ）にも使う。
            // 非 Controller 種別（プロップ/カメラ/ハンド）に解決されると leftCtrl/rightCtrl=false でレーザー自動 suppress。
            HandModelKind leftKind = m_modelSelector.Resolve(true, leftCtx, cycleL);
            HandModelKind rightKind = m_modelSelector.Resolve(false, rightCtx, cycleR);
            // あ〜ん握り見た目: 右手が Hand に解決されたときのみ指 curl を固定（Controller へ切替えたら無効）。
            bool ahhnForCastGrip = ahhnActive && rightKind == HandModelKind.Hand;

            // ドリンク複製グラス: 設定手が Drinking かつ Hand 解決時のみ表示（Controller へ cycle したら非表示）。
            bool drinkUseLeft = Configs.DrinkGlassLeftHand.Value;
            HandModelContext drinkCtx = drinkUseLeft ? leftCtx : rightCtx;
            HandModelKind drinkKind = drinkUseLeft ? leftKind : rightKind;
            bool showGlass = drinkCtx == HandModelContext.Drinking && drinkKind == HandModelKind.Hand;

            // 握り見た目: ハンドモデルの指 curl 入力(grip/trigger)を固定値で上書き（pose/Valid は実値のまま）。
            // HandFingerPoser が snap.GripValue/TriggerValue を曲げ量に使うため、複製に同値を入れると 5 指が一様に握る。
            // あ〜ん=右手 / ドリンク=設定手。要「指を曲げる」ON。left/right 実値は下流(ポインタ/stick)用に温存。
            VrControllerSnapshot leftModel = left;
            VrControllerSnapshot rightModel = right;
            if (ahhnForCastGrip)
            {
                float g = Mathf.Clamp01(Configs.AhhnGripCurl.Value);
                rightModel.GripValue = g;
                rightModel.TriggerValue = g;
            }
            if (showGlass)
            {
                float g = Mathf.Clamp01(Configs.DrinkGlassGripCurl.Value);
                if (drinkUseLeft) { leftModel.GripValue = g; leftModel.TriggerValue = g; }
                else { rightModel.GripValue = g; rightModel.TriggerValue = g; }
            }
            m_controllerModelRunner.Tick(rig, leftModel, rightModel, leftKind, rightKind);

            // 複製グラス本体を手モデルの後に Tick（同 layer・frontmost queue で共存）。
            // snap pose は下流の stick ゼロ化の影響を受けない実値を使う（curl 上書きは見た目専用＝pose 非関与）。
            VrControllerSnapshot drinkSnap = drinkUseLeft ? left : right;
            m_drinkGlassRunner.Tick(rig, drinkSnap, drinkUseLeft, showGlass ? drinkSource : null, drinkProp);

            // Cheki 撮影中: ビューファインダカメラ（物理照準・背面 live 表示）。Phase 1 の override で
            // どちらの手がカメラかは ChekiCameraRightHand で決まっている。撮影中のみアクティブ。
            // stick ゼロ化より前に置き、ズームで camHand.Stick.y を生値で読む。
            VrControllerSnapshot chekiHand = BG2VR.Configs.ChekiCameraRightHand.Value ? right : left;
            bool chekiPhotographing = BG2VR.Configs.ShowChekiCamera.Value
                && BG2VR.UiSceneVoid.MiniGameProbe.IsChekiPhotographing()
                && chekiHand.Valid;
            m_chekiCameraRunner.Tick(rig, chekiHand, chekiPhotographing);

            bool leftCtrl = leftKind == HandModelKind.Controller;
            bool rightCtrl = rightKind == HandModelKind.Controller;

            // ポインタ候補はコントローラ手のみ: grip / ドラッグ / 非コントローラ手のトリガーをゼロ化。
            bool switched = m_handArbiter.Update(
                left.Valid, (gripL || m_adjustWasDragging || !leftCtrl) ? 0f : left.TriggerValue,
                right.Valid, (gripR || m_adjustWasDragging || !rightCtrl) ? 0f : right.TriggerValue,
                Configs.VrFreezeOnset.Value);
            if (switched) m_pointerRunner?.OnPointerHandSwitched();
            VrControllerSnapshot pointerSnap = m_handArbiter.PointerIsLeft ? left : right;
            // レーザー/UI はコントローラ手のみ有効: 非コントローラ or grip 中は suppress（ComputeRay 非 Active＝消灯）。
            bool pointerCtrl = m_handArbiter.PointerIsLeft ? leftCtrl : rightCtrl;
            bool pointerGrip = m_handArbiter.PointerIsLeft ? gripL : gripR;
            bool pointerSuppressed = pointerGrip || !pointerCtrl;

            // ray 計算 → ボタン帯/ドラッグ → buttonsRunner → 配置分岐 → UI 注入 の順（spec §7）。
            Vector3 eyeLocal = CurrentEyeLocal();
            var frame = m_pointerRunner.ComputeRay(pointerSnap, pointerSuppressed);
            var adjust = m_adjustRunner.Tick(frame, pointerSnap, m_panel, eyeLocal, Time.deltaTime);
            m_adjustWasDragging = adjust.Dragging;

            // engage / 曲面トグル＝「最初の操作」。不変条件（spec §6）: m_placed=true 確定は
            // buttonsRunner.Tick より前＝PlaceConfirm は placed ゲートで不発。
            if (adjust.JustEngaged || adjust.CurveToggled) m_placed = true;

            // ドラッグ中はポインタ手の Stick/StickClick を下流から隠す（押し引き専有・誤スクロール/誤 Auto 防止）。
            if (adjust.Dragging)
            {
                if (m_handArbiter.PointerIsLeft) { left.Stick = Vector2.zero; left.StickClick = false; }
                else { right.Stick = Vector2.zero; right.StickClick = false; }
            }
            // grip 中の手の Stick は平行移動へ振り替わる＝nav/RStick からは隠す（「grip 中方向キー無効」）。
            // StickClick(Auto) は据え置き。grip locomotion Tick は上で raw stick を読み終えている。
            if (gripL) left.Stick = Vector2.zero;
            if (gripR) right.Stick = Vector2.zero;

            // 設定パネル表示中: 左スティック縦をパネルのホイールスクロールへ橋渡しし、縦成分を消費する
            // （ゲームの RStick 注入＝左スティック には渡さない・横の free-look は残す＝完全非モーダルを尊重）。
            // 配線位置は grip/調整ドラッグのゼロ化(上)の後・buttonsRunner(下)の前が正＝grip/ドラッグで既に
            // 左 stick がゼロ化済なら deadzone 未満で no-op になり locomotion/ドラッグが優先される（追加ガード不要）。
            if (settings.PanelShown)
            {
                m_settingsRunner.ScrollByStick(left.Stick.y, Configs.SettingsScrollSpeed.Value, Time.deltaTime);
                left.Stick.y = 0f; // 縦のみ消費（横の free-look は残す）
            }

            // VR ボタン割当（決定/戻る/バックログ/スキップ/ナビ/RStick/Auto は bridge へ。再センター/配置確定は戻り値）。
            var cmds = m_buttonsRunner.Tick(left, right, m_handArbiter.PointerIsLeft, Time.deltaTime, m_placed);

            if (adjust.MoveDragging)
            {
                // 不変条件（spec §6）: この移動ドラッグ分岐は配置分岐の最優先（PlaceConfirm /
                // RecenterNow / スライダーより先に評価）。ドラッグ中は手が唯一の姿勢権威
                //（panel は今フレームの m_adjustRunner.Tick が書込済み）。
                // 配置を毎フレ encode → config 書き戻し（.cfg 保存は release 時に一括・
                // PanelAdjustRunner の SaveOnConfigSet 抑止）。遷移 mid-drag は teardown 経路が
                // ここへ到達する前に return するため 1 フレーム前の書き戻し値が残る＝実害なし（spec §8）。
                CaptureEyeOrigin();
                Transform rt = m_panel.RootTransform;
                if (rt != null)
                {
                    var p = PlacementSolver.Encode(rt.localPosition - m_frameOrigin);
                    Configs.WorldUiDistance.Value = p.HorizDist;
                    Configs.WorldUiVerticalOffset.Value = p.Height;
                    Configs.WorldUiYaw.Value = p.YawDeg;
                }
                // 書き戻し値（range clamp 後）を watch キャッシュへ同期＝解放後の誤発火防止。
                m_lastDistance = Configs.WorldUiDistance.Value;
                m_lastOffset = Configs.WorldUiVerticalOffset.Value;
                m_lastYaw = Configs.WorldUiYaw.Value;
                m_lastUprightDeg = Configs.WorldUiUprightAngleDeg.Value;
                // サイズのみ即時反映（姿勢と独立・spec §5）。再センター長押しもドラッグ優先で無視（spec §6）。
                if (Configs.WorldUiSize.Value != m_lastSize)
                {
                    m_lastSize = Configs.WorldUiSize.Value;
                    m_panel.ApplyScale(m_lastSize);
                }
            }
            else
            {
                // 追従可否は m_placed（ボタン意味論）とは独立: WorldUiLockOnTap OFF（既定）は
                // 常時 true＝タップ後も eye 位置追従を継続。ON は !m_placed の間だけ true＝
                // 従来どおりタップ（PlaceConfirm）/ engage で固定（現状と機能的同等）。spec §3。
                bool headFollow = !Configs.WorldUiLockOnTap.Value || !m_placed;

                // PlaceConfirm は m_placed フリップ（ボタン意味論: 未配置→配置済）にのみ使う＝追従とは独立。
                if (!m_placed && cmds.PlaceConfirm) m_placed = true;

                // 再センター: 上ボタン長押し（EnableVrButtons OFF 時は押下即時）でヨーを正面・
                // 高さを既定へ戻す escape hatch（spec §4）。距離はユーザー保存値を維持。config リセットのみ
                // ここで行い、適用（CaptureEyeOrigin/ApplyPlacement）は下の追従/固定分岐に委ねる。
                if (cmds.RecenterNow)
                {
                    Configs.WorldUiYaw.Value = 0f;
                    Configs.WorldUiVerticalOffset.Value = (float)Configs.WorldUiVerticalOffset.DefaultValue;
                }

                if (headFollow)
                {
                    // 追従: 毎フレ eye 位置のみ追従（rig 軸相対＝頭の回転には連動しない静止配置。
                    // 遷移直後の目線高さ settle を吸収・spec §5）。config 保存済みの配置（距離/高さ/ヨー）が
                    // そのまま復元される。再センター時もここで eye 再捕捉＝今の頭の正面へリセットされる。
                    // サイズ/各スライダー変更も ApplyPlacement が config から自動反映する。
                    CaptureEyeOrigin();
                    ApplyPlacement();
                }
                else if (cmds.RecenterNow)
                {
                    // 固定中の再センター: eye 位置を再捕捉してから（=今の頭の正面へ）ヨー0・高さ既定を適用。
                    // CaptureEyeOrigin を省くと停止時点の古い原点基準になり正面に来ない（OLD と同一・機能的同等性のため必須）。
                    CaptureEyeOrigin();
                    ApplyPlacement();
                }
                else if (PoseConfigChanged())
                {
                    // 固定中: 距離/上下/ヨー/直立角スライダーの decode 再配置（config が単一情報源・spec §4）。
                    ApplyPlacement();
                }
                else if (Configs.WorldUiSize.Value != m_lastSize)
                {
                    // 固定中サイズ: F10 スライダー or 拡大ドラッグの書き戻し（同一 watch ＝単一情報源・spec §6）。
                    // ApplyScale は WorldUiSize を書き戻さない＝単方向。スケールのみ反映・手動配置は保持（spec §5）。
                    m_lastSize = Configs.WorldUiSize.Value;
                    m_panel.ApplyScale(m_lastSize);
                }
            }

            // 曲面 watch（姿勢に影響しないため配置 if-else チェーンとは独立。
            // 曲面ボタン/F10 トグル/半径スライダーのどれでも同経路で live 反映・spec §6）。
            bool curvedNow = Configs.WorldUiCurved.Value;
            float radiusNow = Configs.WorldUiCurveRadius.Value;
            if (curvedNow != m_lastCurved || radiusNow != m_lastCurveRadius)
            {
                m_lastCurved = curvedNow;
                m_lastCurveRadius = radiusNow;
                m_panel.SetCurved(curvedNow, radiusNow);
            }

            // 調整ボタンサイズ（比率・加算）watch（距離・幅が不変でも F10 から live 反映・曲面 watch と同型）。
            float buttonRatio = Configs.WorldUiButtonSizeRatio.Value;
            float buttonOffset = Configs.WorldUiButtonSizeOffset.Value;
            if (buttonRatio != m_lastButtonRatio || buttonOffset != m_lastButtonOffset)
            {
                m_lastButtonRatio = buttonRatio;
                m_lastButtonOffset = buttonOffset;
                m_panel.RefreshButtonLayout();
            }

            // depth test watch（F10 → パネル/ボタン/レーザーの material へ live 反映・曲面 watch と同型）。
            // WorldUiDepthTest と VrControllerOccludeUi のどちらが変わっても再 Apply（種別ごとに実効値が分岐）。
            bool depthTestNow = Configs.WorldUiDepthTest.Value;
            bool occludeNow = Configs.VrControllerOccludeUi.Value;
            if (depthTestNow != m_lastDepthTest || occludeNow != m_lastOccludeUi)
            {
                m_lastDepthTest = depthTestNow;
                m_lastOccludeUi = occludeNow;
                // パネル/ボタンは遮蔽 ON で隠れる（純関数で解決）。レーザー/レティクルは種別が分かれるため
                // pointer 側で解決する（線=隠れる / レティクル=最前面）。
                bool panelDepth = UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Panel, occludeNow, depthTestNow);
                m_panel.ApplyDepthTest(panelDepth);
                m_pointerRunner?.ApplyDepthTest();
            }

            // 設定パネル表示中は前面優先: 統一 ray（frame）で設定パネルを raycast。命中なら設定へ橋渡し＋ゲームUI抑止、
            // 外せばゲームUIへ通す（#3）。adjust.SuppressUi（調整帯 hover/ドラッグ中）は前面 raycast を行わない
            // （ゲームUIパネルの調整帯操作を阻害しない）。trigger エッジは frame.Trigger の単一所有＝命中側のみが受ける。
            VrPointerRunner.FrontHit front = default;
            if (settings.PanelShown && frame.Active && !adjust.SuppressUi)
            {
                front = m_settingsRunner.ProcessLaserShared(rig, frame.Origin, frame.Dir,
                    pointerSnap.Trigger, frame.Trigger.JustPressed, frame.Trigger.JustReleased);
            }
            else
            {
                m_settingsRunner.HideLaserExternal(); // 設定非表示/ray 無効/SuppressUi 時は設定レーザーを消灯（hover も解く）
            }

            // ゲーム UI への注入（定常状態のみ。帯 hover/ドラッグ中 or 前面命中時は suppress）。
            m_pointerRunner?.ProcessUi(frame, adjust, front);
        }

        // 戻り値: 投影を起動できたか（UI 未生成なら false ＝呼び出し側が再試行）。
        private bool Setup(Transform rig)
        {
            var canvases = CanvasRootResolver.Resolve();
            if (canvases.Count == 0) return false; // UI 未生成 → 次フレーム再試行（m_active を立てない）

            int cullingMask = ComputeCullingMask(canvases);
            if (cullingMask == 0)
            {
                // 全対象 canvas が Default レイヤ → UI カメラが何も描画せず RT が空になる。
                // spec §5 fallback（専用レイヤ + eye OR）の検討が要る兆候。
                Plugin.Log.LogWarning("[WorldUi] UI カメラ cullingMask=0（対象 canvas が全て Default レイヤ？）。RT が空になる可能性。");
            }

            m_compositor = new VrUiCompositor();
            m_compositor.Create(cullingMask);
            m_compositor.Attach(canvases);

            m_panel = new VrUiPanel();
            m_panel.Create(rig, m_compositor.Texture);
            // 曲面状態は config が単一情報源（幅未確定のうちは内部保留→ApplyPlacement で実体化）。
            m_lastCurved = Configs.WorldUiCurved.Value;
            m_lastCurveRadius = Configs.WorldUiCurveRadius.Value;
            m_panel.SetCurved(m_lastCurved, m_lastCurveRadius);
            m_lastButtonRatio = Configs.WorldUiButtonSizeRatio.Value;
            m_lastButtonOffset = Configs.WorldUiButtonSizeOffset.Value;
            // depth test / コントローラ遮蔽は Create 時に各 material へ適用済み＝watch キャッシュを現在値で初期化。
            m_lastDepthTest = Configs.WorldUiDepthTest.Value;
            m_lastOccludeUi = Configs.VrControllerOccludeUi.Value;

            // (再)構築時は現在の目線高さで配置（rig 軸相対＝頭の向きに依存しない）。
            // 手動アンカーがあればプレイヤー相対の同位置へ復元（spec §5/§7）。
            CaptureEyeOrigin();
            ApplyPlacement();

            m_boundRig = rig;
            m_pointerRunner = new VrPointerRunner();
            m_pointerRunner.Setup(rig, m_panel);
            // (再)build 直後は未配置＝頭向きに追従。最初の操作で world 固定（spec Task2: 操作するまで正面追従）。
            m_placed = false;
            m_active = true;
            Plugin.Log.LogInfo($"[WorldUi] projector 起動: canvas {canvases.Count} 枚を合成投影。");
            return true;
        }

        private void Teardown()
        {
            if (m_pointerRunner != null) { m_pointerRunner.Teardown(); m_pointerRunner = null; }
            if (m_panel != null) { m_panel.Destroy(); m_panel = null; }
            if (m_compositor != null) { m_compositor.Destroy(); m_compositor = null; }
            m_boundRig = null;
            m_active = false;
            m_placed = false;
            m_buttonsRunner.Teardown(); // bridge 全消灯 + ボタン状態リセット（arbiter は持続・フィールド宣言のコメント参照）
            m_karaokeShakeRunner.Reset(); // カラオケ state を破棄（遷移またぎの armed 残留防止）
            m_handSumoPushRunner.Reset(); // 手押し相撲 state を破棄（遷移またぎの armed 残留防止）
            m_gripRunner.Reset(); // rig 破棄と同時に grip 状態/累積も破棄（新 rig は初期 pose）
            m_adjustRunner.Reset(); // ドラッグ状態破棄 + SaveOnConfigSet 復元。配置は config が保持＝遷移またぎ復元
            m_adjustWasDragging = false;
        }

        // 目線位置（rig-local）。頭の回転は読まない＝配置は常に rig 軸（シーン正面）相対。
        private static Vector3 CurrentEyeLocal()
        {
            Camera eye = VRModCore.GetVrEyeCamera();
            return eye != null ? eye.transform.localPosition : Vector3.zero;
        }

        // 配置フレーム原点の確定（配置イベント時のみ更新＝encode/decode の基準を凍結する）。
        private void CaptureEyeOrigin() => m_frameOrigin = CurrentEyeLocal();

        private void ApplyPlacement()
        {
            m_lastDistance = Configs.WorldUiDistance.Value;
            m_lastSize = Configs.WorldUiSize.Value;
            m_lastOffset = Configs.WorldUiVerticalOffset.Value;
            m_lastYaw = Configs.WorldUiYaw.Value;
            m_lastUprightDeg = Configs.WorldUiUprightAngleDeg.Value;
            // config 3 スカラー → 視点基準オフセット → 自動向き（直立帯 + ドーム・spec §3 §4）。
            // フレームは常に rig 軸（回転 identity）＝配置が頭の向きに依存しない。
            Vector3 offset = PlacementSolver.Decode(m_lastDistance, m_lastOffset, m_lastYaw);
            Quaternion rot = PlacementSolver.ComputeRotation(offset, m_lastUprightDeg);
            m_panel.ApplyPose(m_frameOrigin + offset, rot);
            m_panel.ApplyScale(m_lastSize);
            m_panel.UpdateEyeDistance(m_frameOrigin);
        }

        // 距離/上下/ヨー/直立角 = 姿勢に効く config。サイズはスケールのみ＝分離判定。
        private bool PoseConfigChanged()
            => Configs.WorldUiDistance.Value != m_lastDistance
            || Configs.WorldUiVerticalOffset.Value != m_lastOffset
            || Configs.WorldUiYaw.Value != m_lastYaw
            || Configs.WorldUiUprightAngleDeg.Value != m_lastUprightDeg;

        private void OnDestroy()
        {
            m_settingsRunner.Teardown(); // RT redirect 解除 + quad/RT 破棄（targetTexture を残さない）
            Teardown();
        }

        private static int ComputeCullingMask(List<Canvas> canvases)
        {
            int mask = 0;
            // relayer 後の実効 layer（Default→UI(5)）で和集合を取る。ManagedCanvas.Convert と同一規則。
            foreach (var c in canvases) { if (c == null) continue; mask |= (1 << CanvasLayerPolicy.EffectiveLayer(c.gameObject.layer)); }
            mask &= ~(1 << CanvasLayerPolicy.DefaultLayer); // quad(Default) は除外（EffectiveLayer 後は 0 が無いので実質防御）
            return mask;
        }
    }
}
