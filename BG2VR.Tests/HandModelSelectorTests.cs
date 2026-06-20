using Xunit;
using BG2VR.VrInput;

public class HandModelSelectorTests
{
    // ── 通常コンテキスト ──
    [Fact]
    public void Normal_DefaultBothControllers()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Controller, s.Resolve(true,  HandModelContext.Normal, false));
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Normal, false));
    }

    [Fact]
    public void Normal_CycleAdvancesAndWraps()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Hand,       s.Resolve(true, HandModelContext.Normal, true));  // Controller -> Hand
        Assert.Equal(HandModelKind.Controller, s.Resolve(true, HandModelContext.Normal, true));  // Hand -> Controller（2 値 wrap）
    }

    [Fact]
    public void Normal_CycleIsPerHand()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Hand,       s.Resolve(true,  HandModelContext.Normal, true));  // 左のみ進める
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Normal, false)); // 右は不変
    }

    [Fact]
    public void Normal_NeverProducesSpecialKinds()
    {
        // Camera / Tambourine / GlowStick は Normal ループに絶対に出ない。
        var s = new HandModelSelector();
        for (int i = 0; i < 10; i++)
        {
            var l = s.Resolve(true,  HandModelContext.Normal, true);
            var r = s.Resolve(false, HandModelContext.Normal, true);
            Assert.True(l == HandModelKind.Controller || l == HandModelKind.Hand);
            Assert.True(r == HandModelKind.Controller || r == HandModelKind.Hand);
        }
    }

    // ── カラオケ（左右でプロップが異なる） ──
    [Fact]
    public void Karaoke_EntersAtPropPerHand()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Tambourine, s.Resolve(true,  HandModelContext.Karaoke, false)); // 左=タンバリン
        Assert.Equal(HandModelKind.GlowStick,  s.Resolve(false, HandModelContext.Karaoke, false)); // 右=サイリウム
    }

    [Fact]
    public void Karaoke_CyclesPropControllerHand()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.GlowStick,  s.Resolve(false, HandModelContext.Karaoke, false)); // 突入=サイリウム
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Karaoke, true));  // -> Controller
        Assert.Equal(HandModelKind.Hand,       s.Resolve(false, HandModelContext.Karaoke, true));  // -> Hand
        Assert.Equal(HandModelKind.GlowStick,  s.Resolve(false, HandModelContext.Karaoke, true));  // -> サイリウム（3 値 wrap）
    }

    // ── Cheki（カメラ起点 3 値ループ） ──
    [Fact]
    public void Cheki_CyclesCameraControllerHand()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Camera,     s.Resolve(false, HandModelContext.Cheki, false));
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Cheki, true));
        Assert.Equal(HandModelKind.Hand,       s.Resolve(false, HandModelContext.Cheki, true));
        Assert.Equal(HandModelKind.Camera,     s.Resolve(false, HandModelContext.Cheki, true));
    }

    // ── 手押し相撲 / あ〜ん（ハンド起点 2 値ループ） ──
    [Fact]
    public void HandSumo_CyclesHandController()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Hand,       s.Resolve(true, HandModelContext.HandSumo, false));
        Assert.Equal(HandModelKind.Controller, s.Resolve(true, HandModelContext.HandSumo, true));
        Assert.Equal(HandModelKind.Hand,       s.Resolve(true, HandModelContext.HandSumo, true)); // wrap
    }

    [Fact]
    public void Ahhn_CyclesHandController()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Hand,       s.Resolve(false, HandModelContext.Ahhn, false));
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Ahhn, true));
        Assert.Equal(HandModelKind.Hand,       s.Resolve(false, HandModelContext.Ahhn, true)); // wrap
    }

    // ── 突入リセット ──
    [Fact]
    public void Context_ResetsToDefaultOnReentry()
    {
        var s = new HandModelSelector();
        s.Resolve(false, HandModelContext.Karaoke, false); // GlowStick
        s.Resolve(false, HandModelContext.Karaoke, true);  // Controller
        s.Resolve(false, HandModelContext.Karaoke, true);  // Hand
        s.Resolve(false, HandModelContext.Normal,  false); // 離脱（通常へ）
        // 再突入 → 既定（サイリウム）へリセット
        Assert.Equal(HandModelKind.GlowStick, s.Resolve(false, HandModelContext.Karaoke, false));
    }

    [Fact]
    public void Context_DoesNotResetWhileStaying()
    {
        // 同一コンテキストに留まる間は選択を保持（毎フレ Resolve でリセットしない）。
        var s = new HandModelSelector();
        s.Resolve(false, HandModelContext.Karaoke, false); // GlowStick
        s.Resolve(false, HandModelContext.Karaoke, true);  // Controller
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Karaoke, false)); // 保持
    }

    [Fact]
    public void Context_ResetsWhenSwitchingBetweenSpecials()
    {
        // 特殊→別の特殊（稀: Karaoke→Cheki）でも立上りでリセットされ既定から始まる。
        var s = new HandModelSelector();
        s.Resolve(false, HandModelContext.Karaoke, true); // GlowStick -> Controller
        Assert.Equal(HandModelKind.Camera, s.Resolve(false, HandModelContext.Cheki, false)); // Cheki 既定へリセット
    }

    [Fact]
    public void Context_EntryWithCycleSameFrame_ResetsThenAdvances()
    {
        // 突入と同フレームに cycleRequested（grip+トリガー保持のまま突入した稀ケース）。
        // 処理順 reset（先頭へ）→ cycle（+1）→ get ＝ order[1] が返る（設計書 §4.4 許容挙動の回帰固定）。
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Karaoke, true)); // GlowStick -> Controller
    }

    // ── 通常選択は特殊コンテキストの操作で不変 ──
    [Fact]
    public void Normal_SelectionPreservedAcrossContext()
    {
        var s = new HandModelSelector();
        s.Resolve(true, HandModelContext.Normal, true); // 通常を Hand に
        Assert.Equal(HandModelKind.Hand, s.Resolve(true, HandModelContext.Normal, false));
        s.Resolve(true, HandModelContext.Karaoke, false); // Tambourine
        s.Resolve(true, HandModelContext.Karaoke, true);  // Controller（特殊側を操作）
        Assert.Equal(HandModelKind.Hand, s.Resolve(true, HandModelContext.Normal, false)); // 通常は Hand のまま
    }

    [Fact]
    public void Context_IsPerHand()
    {
        // カラオケ中、左右の選択が干渉しない。
        var s = new HandModelSelector();
        s.Resolve(true,  HandModelContext.Karaoke, true);  // 左: Tambourine -> Controller
        Assert.Equal(HandModelKind.Controller, s.Resolve(true,  HandModelContext.Karaoke, false));
        Assert.Equal(HandModelKind.GlowStick,  s.Resolve(false, HandModelContext.Karaoke, false)); // 右は既定のまま
    }

    // ── ドリンク（設定手のみ・Hand 既定→Controller へ cycle 可） ──
    [Fact]
    public void Drinking_EntersAtHand()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Hand, s.Resolve(false, HandModelContext.Drinking, false)); // 右手=ハンド既定
    }

    [Fact]
    public void Drinking_CyclesToControllerAndWraps()
    {
        var s = new HandModelSelector();
        Assert.Equal(HandModelKind.Hand,       s.Resolve(false, HandModelContext.Drinking, false));
        Assert.Equal(HandModelKind.Controller, s.Resolve(false, HandModelContext.Drinking, true)); // Hand -> Controller
        Assert.Equal(HandModelKind.Hand,       s.Resolve(false, HandModelContext.Drinking, true)); // 2 値 wrap
    }

    [Fact]
    public void Drinking_ResetsToHandOnReentry()
    {
        var s = new HandModelSelector();
        s.Resolve(false, HandModelContext.Drinking, false); // enter -> Hand
        s.Resolve(false, HandModelContext.Drinking, true);  // -> Controller
        s.Resolve(false, HandModelContext.Normal,   false); // 一旦 Normal へ抜ける（NPC が飲み終えた）
        Assert.Equal(HandModelKind.Hand, s.Resolve(false, HandModelContext.Drinking, false)); // 再突入で Hand へリセット
    }

    [Fact]
    public void Drinking_OtherHandStaysNormal()
    {
        var s = new HandModelSelector();
        s.Resolve(false, HandModelContext.Drinking, false); // 右手=ドリンク
        Assert.Equal(HandModelKind.Controller, s.Resolve(true, HandModelContext.Normal, false)); // 左手は既定 Controller のまま
    }
}
