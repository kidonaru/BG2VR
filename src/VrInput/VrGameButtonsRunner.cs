using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrInput
{
    /// <summary>
    /// 会話 UX ボタン割当の毎フレ統括（ゲームパッド準拠・spec §3）。両手 snapshot から
    /// 右 A=決定 / 右 B 短押し=戻る・長押し=再センター / 左 X=バックログ / 左 Y hold=既読スキップ /
    /// 右スティック=4方向ナビ / 左スティック=RStick / 押し込み=オート切替 を判定し
    /// GbGameButtonBridge へ書き込む。再センター/配置確定はパネル配置の責務を持つ ProjectorRunner へ
    /// 戻り値で返す。ProjectorRunner の定常状態でのみ Tick される（非定常フレームの bridge 失効は
    /// ProjectorRunner.Update 先頭の無条件 Clear が担う）。
    /// ドラッグ中のポインタ手 Stick/StickClick は ProjectorRunner が struct copy 上でゼロ化済み
    /// （押し引き専有・spec §6-6）＝ここでは特別扱い不要。
    /// </summary>
    internal sealed class VrGameButtonsRunner
    {
        public struct UiCommands
        {
            public bool RecenterNow;  // 右 B 長押し（配置済・EnableVrButtons OFF 時は上ボタン押下即時）
            public bool PlaceConfirm; // 未配置時の確定操作（B / Y 押下 or ポインタ手トリガー）
        }

        private readonly HoldButtonClassifier m_upperRight = new HoldButtonClassifier(); // 右 B: 短=戻る / 長=再センター
        private readonly PointerButtonState m_upperLeft = new PointerButtonState();      // 左 Y: hold=スキップ / JustPressed=配置確定
        private readonly PointerButtonState m_lowerRight = new PointerButtonState();     // 右 A: 決定
        private readonly PointerButtonState m_lowerLeft = new PointerButtonState();      // 左 X: バックログ
        private readonly PointerButtonState m_stickClickLeft = new PointerButtonState();
        private readonly PointerButtonState m_stickClickRight = new PointerButtonState();
        private readonly PointerButtonState m_placeTrigger = new PointerButtonState();
        private readonly PointerButtonState m_menu = new PointerButtonState();           // 左メニューボタン: ポーズメニュー開閉
        private readonly StickNavMapper m_nav = new StickNavMapper();

        public UiCommands Tick(VrControllerSnapshot left, VrControllerSnapshot right,
            bool pointerIsLeft, float dt, bool placed)
        {
            var cmds = new UiCommands();
            bool enabled = global::BG2VR.Configs.EnableVrButtons.Value;

            // 状態機械は常に毎フレーム更新する（enabled 分岐で止めると OFF→ON 切替時に偽エッジが出る）。
            var upR = m_upperRight.Update(right.Valid && right.B, dt);
            var upL = m_upperLeft.Update(left.Valid && left.B);
            var loR = m_lowerRight.Update(right.Valid && right.A);
            var loL = m_lowerLeft.Update(left.Valid && left.A);
            var scL = m_stickClickLeft.Update(left.Valid && left.StickClick);
            var scR = m_stickClickRight.Update(right.Valid && right.StickClick);
            var menu = m_menu.Update(left.Valid && left.Menu); // 左メニューボタン（エッジは常時更新＝OFF→ON 偽エッジ防止）
            VrControllerSnapshot pointerSnap = pointerIsLeft ? left : right;
            var trig = m_placeTrigger.Update(pointerSnap.Valid && pointerSnap.Trigger);
            // nav=右スティック / RStick=左スティック（左右スワップ）。grip 中の手の Stick は
            // ProjectorRunner が呼び出し前にゼロ化済み＝平行移動へ振替時は自動で抑制される
            //（＝左手 grip 中は left.Stick がゼロ化され RStick も不能＝その手の平行移動が専有）。
            var nav = m_nav.Update(right.Valid ? right.Stick : Vector2.zero,
                global::BG2VR.Configs.VrNavStickThreshold.Value);
            Vector2 rStick = left.Valid ? left.Stick : Vector2.zero;
            float dz = global::BG2VR.Configs.VrRStickDeadzone.Value;
            if (rStick.sqrMagnitude < dz * dz) rStick = Vector2.zero;

            bool backPulse = false;
            if (!placed)
            {
                // 未配置: 上ボタン（右 B / 左 Y）or ポインタ手トリガーで配置確定（Phase3 の両手対称版を維持）。
                // B は consume してリリースまで戻る/再センターへの連鎖発火を防ぐ。
                // Y は level（スキップ）なので consume 不要（押下継続中のスキップ注入は仕様どおり・spec §6-4）。
                if (upR.JustPressed || upL.JustPressed || trig.JustPressed)
                {
                    cmds.PlaceConfirm = true;
                    if (upR.JustPressed) m_upperRight.ConsumePress();
                }
            }
            else if (enabled)
            {
                cmds.RecenterNow = upR.LongPress;
                backPulse = upR.ShortPress;
            }
            else
            {
                // 注入 OFF: Phase3 までの挙動（上ボタン押下=即時再センター）へフォールバック（spec §4.3）。
                cmds.RecenterNow = upR.JustPressed || upL.JustPressed;
            }

            if (enabled)
            {
                GbGameButtonBridge.DecidePulse = loR.JustPressed;
                GbGameButtonBridge.BackPulse = backPulse;
                GbGameButtonBridge.BacklogPulse = loL.JustPressed;
                GbGameButtonBridge.SkipHeld = upL.Pressed;
                GbGameButtonBridge.AutoPulse = scL.JustPressed || scR.JustPressed;
                // カラオケ IN_GAME 中は方向ナビ注入を抑止（Up/Down/Left/Right は IsNoteInput を満たす＝
                // スティック操作で幻のタンバリン判定が出る。音符入力は ZL/ZR 振りのみ・spec §5.5）。
                // IsKaraokeInGame で IN_GAME に限定＝サスペンドダイアログ(SUSPEND)中のナビ操作は温存（plan-review 🟡2）。
                bool navAllowed = !BG2VR.UiSceneVoid.MiniGameProbe.IsKaraokeInGame();
                GbGameButtonBridge.NavUpPulse = navAllowed && nav.UpPulse;
                GbGameButtonBridge.NavDownPulse = navAllowed && nav.DownPulse;
                GbGameButtonBridge.NavLeftPulse = navAllowed && nav.LeftPulse;
                GbGameButtonBridge.NavRightPulse = navAllowed && nav.RightPulse;
                GbGameButtonBridge.NavUpHeld = navAllowed && nav.UpHeld;
                GbGameButtonBridge.NavDownHeld = navAllowed && nav.DownHeld;
                GbGameButtonBridge.NavLeftHeld = navAllowed && nav.LeftHeld;
                GbGameButtonBridge.NavRightHeld = navAllowed && nav.RightHeld;
                GbGameButtonBridge.RStickValue = rStick;
            }

            // ポーズメニュー開閉は会話 UX ボタン群(EnableVrButtons)から独立 gate（AND しない）。
            // 理由: ポーズメニュー=タイトル/オプションへの唯一の入口＝会話ボタンを切っても残すべき最後の砦。
            // 注入＝実 Start 押下と等価で、ゲーム各シーンの開閉ガード・即閉じ防止(0.2s 窓)・ミニゲーム中
            // 抑止を全て再利用する。OFF 時は何も書かない（前フレ失効は ProjectorRunner.Update 冒頭 Clear が担保）。
            if (global::BG2VR.Configs.EnableVrPauseButton.Value)
                GbGameButtonBridge.StartPulse = menu.JustPressed;

            // OFF 経路に else を置かない（敢えて Clear しない）。当 runner は OFF 時に自分のフィールドを
            // 何も書かず、前フレ分の失効は ProjectorRunner.Update 冒頭の無条件 GbGameButtonBridge.Clear() が担保する。
            // ここで Clear() すると、同フレーム先行で KaraokeShakeRunner/HandSumoPushRunner が set した
            // NotePulseZL/ZR・HandSumoPushPulse まで破壊する（buttonsRunner.Tick はそれらより後段）ため。
            return cmds;
        }

        public void Teardown()
        {
            GbGameButtonBridge.Clear();
            m_upperRight.Reset();
            m_upperLeft.Reset();
            m_lowerRight.Reset();
            m_lowerLeft.Reset();
            m_stickClickLeft.Reset();
            m_stickClickRight.Reset();
            m_placeTrigger.Reset();
            m_menu.Reset();
            m_nav.Reset();
        }
    }
}
