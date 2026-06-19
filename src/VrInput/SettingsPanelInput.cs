using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// VR コントローラ入力 → 設定パネル（F10）操作へのマッピング純ロジック（UnityEngine.Vector2 + System のみ）。
    /// snapshot からフィールド抽出した primitive を受け、SettingsView の既存ナビ handler に対応する
    /// アクション列（NavActions）を返す。Configs は読まず解決済み値を引数で受ける（既存純関数規約）。
    ///
    /// 割当（spec §5）:
    ///   両 stick-click hold(toggleHoldSec) → 開閉トグル / 右スティック4方向 → 行移動・スライダー(repeat) /
    ///   右A → 決定 / 左A → カテゴリ前 / 左B → カテゴリ次 / 右トリガー hold → shift(粗い増減)。
    /// 閉じるは開閉コンボに一本化（右 B は使わない）: モーダル離脱フレームでゲーム入力が復帰する瞬間に
    /// B 押下保持があるとゲームの「戻る」が誤発火するため。コンボは ConsumeStickClick ラッチで境界も安全。
    /// 入力アービトレーション（§6.5）は呼び出し側 ProjectorRunner の責務（表示中はゲーム入力 runner を skip）。
    /// </summary>
    public sealed class SettingsPanelInput
    {
        public struct Inputs
        {
            public bool LeftValid, RightValid;
            public Vector2 RightStick;                // 4 方向ナビ
            public bool LeftStickClick, RightStickClick; // 開閉コンボ
            public bool LeftA, LeftB, RightA, RightB;
            public bool RightTrigger;                 // shift 修飾（粗い増減）
            public bool PanelShown;                   // 現在パネル表示中か（hidden 中はコンボのみ有効）
        }

        public struct NavActions
        {
            public bool ToggleOpen;        // 開閉トグル（両 stick-click hold 達成の rising 1 回）
            public bool ConsumeStickClick; // 開閉コンボ hold 中＝game Auto へ stick-click を渡さない
            public bool RowUp, RowDown;    // 行移動（repeat 込み）
            public bool SlideLeft, SlideRight; // スライダー増減（repeat 込み）
            public bool Shift;             // 粗い増減（右トリガー hold）
            public bool Confirm;           // 決定（右 A rising）
            public bool TabPrev, TabNext;  // カテゴリ前/次（左 A / 左 B rising）
        }

        private readonly StickNavMapper m_nav = new StickNavMapper();
        private readonly NavRepeat m_repUp = new NavRepeat();
        private readonly NavRepeat m_repDown = new NavRepeat();
        private readonly NavRepeat m_repLeft = new NavRepeat();
        private readonly NavRepeat m_repRight = new NavRepeat();
        private readonly PointerButtonState m_confirm = new PointerButtonState();
        private readonly PointerButtonState m_tabPrev = new PointerButtonState();
        private readonly PointerButtonState m_tabNext = new PointerButtonState();

        private float m_comboTimer;
        private bool m_comboLatched; // toggle 済（両 stick-click を離すまで再発火しない）

        public NavActions Update(Inputs i, float navThreshold, float repeatDelay,
            float repeatInterval, float toggleHoldSec, float dt)
        {
            var a = new NavActions();

            // ── 開閉コンボ（両 stick-click を toggleHoldSec 保持で 1 回トグル）。IsShown に依らず常時評価 ──
            bool comboHeld = i.LeftValid && i.RightValid && i.LeftStickClick && i.RightStickClick;
            if (comboHeld)
            {
                a.ConsumeStickClick = true; // hold 中ずっと消費（single stick-click=Auto の二重発火抑止）
                if (!m_comboLatched)
                {
                    m_comboTimer += dt;
                    if (m_comboTimer >= toggleHoldSec) { a.ToggleOpen = true; m_comboLatched = true; }
                }
            }
            else
            {
                m_comboTimer = 0f;
                m_comboLatched = false;
            }

            if (i.PanelShown)
            {
                var nav = m_nav.Update(i.RightValid ? i.RightStick : Vector2.zero, navThreshold);
                a.RowUp = m_repUp.Update(nav.UpHeld, repeatDelay, repeatInterval, dt);
                a.RowDown = m_repDown.Update(nav.DownHeld, repeatDelay, repeatInterval, dt);
                a.SlideLeft = m_repLeft.Update(nav.LeftHeld, repeatDelay, repeatInterval, dt);
                a.SlideRight = m_repRight.Update(nav.RightHeld, repeatDelay, repeatInterval, dt);
                a.Shift = i.RightTrigger;
                a.Confirm = m_confirm.Update(i.RightValid && i.RightA).JustPressed;
                a.TabPrev = m_tabPrev.Update(i.LeftValid && i.LeftA).JustPressed;
                a.TabNext = m_tabNext.Update(i.LeftValid && i.LeftB).JustPressed;
            }
            else
            {
                // hidden 中も状態機械を空送り（held/edge を進めて表示開始フレームの偽エッジ・偽連射を防ぐ）。
                m_nav.Update(Vector2.zero, navThreshold);
                m_repUp.Update(false, repeatDelay, repeatInterval, dt);
                m_repDown.Update(false, repeatDelay, repeatInterval, dt);
                m_repLeft.Update(false, repeatDelay, repeatInterval, dt);
                m_repRight.Update(false, repeatDelay, repeatInterval, dt);
                m_confirm.Update(false);
                m_tabPrev.Update(false);
                m_tabNext.Update(false);
            }
            return a;
        }

        public void Reset()
        {
            m_nav.Reset();
            m_repUp.Reset(); m_repDown.Reset(); m_repLeft.Reset(); m_repRight.Reset();
            m_confirm.Reset(); m_tabPrev.Reset(); m_tabNext.Reset();
            m_comboTimer = 0f;
            m_comboLatched = false;
        }
    }
}
