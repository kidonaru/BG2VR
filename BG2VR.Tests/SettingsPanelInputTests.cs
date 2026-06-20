using BG2VR.VrInput;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests;

public class SettingsPanelInputTests
{
    private const float Th = 0.5f;      // navThreshold
    private const float Delay = 0.4f;
    private const float Interval = 0.12f;
    private const float Hold = 0.6f;    // toggleHoldSec
    private const float Dt = 0.3f;

    private static SettingsPanelInput.Inputs Base(bool shown) => new SettingsPanelInput.Inputs
    {
        LeftValid = true,
        RightValid = true,
        PanelShown = shown,
    };

    // ── 開閉コンボ ──────────────────────────────────────────────
    [Fact]
    public void 両stickclick_hold達成でToggleOpenが1回だけ()
    {
        var sp = new SettingsPanelInput();
        var i = Base(false);
        i.LeftStickClick = true; i.RightStickClick = true;

        var a1 = sp.Update(i, Th, Delay, Interval, Hold, Dt); // timer 0.3
        Assert.False(a1.ToggleOpen);
        Assert.True(a1.ConsumeStickClick);

        var a2 = sp.Update(i, Th, Delay, Interval, Hold, Dt); // timer 0.6 → 達成
        Assert.True(a2.ToggleOpen);

        var a3 = sp.Update(i, Th, Delay, Interval, Hold, Dt); // latched → 再発火しない
        Assert.False(a3.ToggleOpen);
        Assert.True(a3.ConsumeStickClick);
    }

    [Fact]
    public void 片手のみのstickclickではToggleしない()
    {
        var sp = new SettingsPanelInput();
        var i = Base(false);
        i.LeftStickClick = true; i.RightStickClick = false;
        for (int n = 0; n < 5; n++)
        {
            var a = sp.Update(i, Th, Delay, Interval, Hold, Dt);
            Assert.False(a.ToggleOpen);
            Assert.False(a.ConsumeStickClick);
        }
    }

    [Fact]
    public void release後に再holdするとまたToggleできる()
    {
        var sp = new SettingsPanelInput();
        var held = Base(false); held.LeftStickClick = true; held.RightStickClick = true;
        var released = Base(false);

        sp.Update(held, Th, Delay, Interval, Hold, Dt);
        Assert.True(sp.Update(held, Th, Delay, Interval, Hold, Dt).ToggleOpen); // 1回目達成
        sp.Update(released, Th, Delay, Interval, Hold, Dt); // 離す → latch 解除
        sp.Update(held, Th, Delay, Interval, Hold, Dt);     // timer 0.3
        Assert.True(sp.Update(held, Th, Delay, Interval, Hold, Dt).ToggleOpen); // 2回目達成
    }

    // ── ナビ（表示中のみ） ──────────────────────────────────────
    [Fact]
    public void 右スティック上でRowUp_即時()
    {
        var sp = new SettingsPanelInput();
        var i = Base(true);
        i.RightStick = new Vector2(0f, 1f);
        var a = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.True(a.RowUp);
        Assert.False(a.RowDown);
    }

    [Fact]
    public void 右スティック右でSlideRight_即時()
    {
        var sp = new SettingsPanelInput();
        var i = Base(true);
        i.RightStick = new Vector2(1f, 0f);
        var a = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.True(a.SlideRight);
        Assert.False(a.SlideLeft);
    }

    [Fact]
    public void hidden中はナビが出ない()
    {
        var sp = new SettingsPanelInput();
        var i = Base(false);
        i.RightStick = new Vector2(0f, 1f);
        var a = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.False(a.RowUp);
        Assert.False(a.SlideRight);
    }

    // ── ボタン（rising edge・表示中） ──────────────────────────
    [Fact]
    public void 右AでConfirm_立ち上がりのみ()
    {
        var sp = new SettingsPanelInput();
        var i = Base(true); i.RightA = true;
        var a1 = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.True(a1.Confirm);
        // 押しっぱなしでは再発火しない
        var a2 = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.False(a2.Confirm);
    }

    [Fact]
    public void 右Bは閉じ操作に使わない_撤去済み()
    {
        // 右 B はモーダル離脱境界でのゲーム「戻る」誤発火を避けるため設定操作に割当てない。
        // NavActions に Close フィールドが無いこと自体が回帰防止（コンパイル時保証）。閉じるはコンボのみ。
        var sp = new SettingsPanelInput();
        var i = Base(true); i.RightB = true;
        var a = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.False(a.Confirm); // 右 B は決定にもならない
    }

    [Fact]
    public void 左AでTabPrev_左BでTabNext()
    {
        var sp = new SettingsPanelInput();
        var i = Base(true); i.LeftA = true; i.LeftB = true;
        var a = sp.Update(i, Th, Delay, Interval, Hold, Dt);
        Assert.True(a.TabPrev);
        Assert.True(a.TabNext);
    }

    [Fact]
    public void 右トリガーでShift()
    {
        var sp = new SettingsPanelInput();
        var i = Base(true); i.RightTrigger = true;
        Assert.True(sp.Update(i, Th, Delay, Interval, Hold, Dt).Shift);
    }

    [Fact]
    public void hidden中のボタンは無効_表示後の初回押下はrising扱い()
    {
        var sp = new SettingsPanelInput();
        var hiddenPressed = Base(false); hiddenPressed.RightA = true;
        // hidden 中に A 押下 → Confirm 出ない
        Assert.False(sp.Update(hiddenPressed, Th, Delay, Interval, Hold, Dt).Confirm);
        // 表示に切替え A 継続押下 → 表示後の初回は rising 扱いで Confirm
        var shownPressed = Base(true); shownPressed.RightA = true;
        Assert.True(sp.Update(shownPressed, Th, Delay, Interval, Hold, Dt).Confirm);
    }
}
