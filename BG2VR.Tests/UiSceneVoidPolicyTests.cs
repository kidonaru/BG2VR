using BG2VR;
using BG2VR.UiSceneVoid;
using Xunit;

namespace BG2VR.Tests
{
    public class UiSceneVoidPolicyTests
    {
        // --- ShouldVoid: menu シーン（フルスクリーン UI 専用）= SteelFrame 以外は全 void ---

        [Theory]
        [InlineData("HomeScene")]
        [InlineData("TitleScene")]
        [InlineData("ExtraScene")]
        [InlineData("StaffCreditScene")]
        [InlineData("FirstScene")]
        public void ShouldVoid_menuシーンはSteelFrame以外の全envでtrue(string name)
        {
            Assert.True(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.None, miniGameStages3D: false));   // boot 過渡
            Assert.True(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.Hole, miniGameStages3D: false));   // bar 残留
            Assert.True(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.Talk2D, miniGameStages3D: false)); // デート後の舞台残留（v2 の動機）
            Assert.True(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.Other, miniGameStages3D: false));  // VipRoom/GameRoom/未知型 残留
        }

        [Theory]
        [InlineData("HomeScene")] // 鉄骨ミニゲーム（menu 唯一の合法 3D）
        [InlineData("TitleScene")]
        [InlineData("ExtraScene")]
        [InlineData("StaffCreditScene")]
        [InlineData("FirstScene")]
        public void ShouldVoid_menuシーンでもSteelFrameはfalse(string name)
        {
            Assert.False(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.SteelFrame, miniGameStages3D: false));
        }

        // --- ShouldVoid: 3D ミニゲーム上演中（エクストラ発カラオケ等）は 3D が主役＝無条件で void しない ---
        // （2D 演出の ASMR は probe 側で miniGameStages3D=false に落ちる＝void 維持。ゲーム型依存のため policy テスト対象外）

        [Theory]
        [InlineData("ExtraScene", EnvKind.Other)] // エクストラ発カラオケ（VipRoom 上演）が真っ黒になった実バグ 2026-06-07
        [InlineData("ExtraScene", EnvKind.Hole)]
        [InlineData("HomeScene", EnvKind.Other)]
        [InlineData("AfterScene", EnvKind.Hole)]  // event シーン側のルールよりも優先
        public void ShouldVoid_3Dミニゲーム上演中は無条件でfalse(string name, EnvKind kind)
        {
            Assert.False(UiSceneVoidPolicy.ShouldVoid(name, kind, miniGameStages3D: true));
        }

        // --- ShouldVoid: event シーン（Talk2D 上演）= 確実に leak と言える Hole のみ void ---

        [Theory]
        [InlineData("AfterScene")]
        [InlineData("HolidayAfterScene")]
        [InlineData("WeekdayEncountScene")]
        [InlineData("PrologueScene")]
        [InlineData("EpilogueScene")]
        public void ShouldVoid_eventシーンはHoleのみtrue(string name)
        {
            Assert.True(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.Hole, miniGameStages3D: false)); // 選択フェーズの bar 残留
            Assert.False(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.Talk2D, miniGameStages3D: false)); // 上演中（区別不能のため保守側）
            Assert.False(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.None, miniGameStages3D: false));
            Assert.False(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.SteelFrame, miniGameStages3D: false));
            Assert.False(UiSceneVoidPolicy.ShouldVoid(name, EnvKind.Other, miniGameStages3D: false));
        }

        // --- ShouldVoid: 3D 提示シーン・過渡・未知名は void しない（安全側） ---

        [Theory]
        [InlineData("BarScene")]           // bar gameplay（env=Hole を意図的に表示）
        [InlineData("EscortedEntryScene")] // 同伴イベント（3D キャラ表示）
        [InlineData(null)]
        [InlineData("")]
        [InlineData("UnknownDlcScene")]
        [InlineData("homescene")] // 大文字小文字は区別（Scene.name は固定表記）
        public void ShouldVoid_対象外シーンは全envでfalse(string name)
        {
            foreach (EnvKind kind in System.Enum.GetValues(typeof(EnvKind)))
            {
                Assert.False(UiSceneVoidPolicy.ShouldVoid(name, kind, miniGameStages3D: false));
            }
        }
    }
}
