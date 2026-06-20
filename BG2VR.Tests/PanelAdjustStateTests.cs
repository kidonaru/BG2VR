using Xunit;
using BG2VR.WorldUi;

namespace BG2VR.Tests
{
    public class PanelAdjustStateTests
    {
        [Fact]
        public void hover中のrising_でengage()
        {
            var s = new PanelAdjustState();
            var r = s.Update(true, true, true, PanelButtonKind.Move);
            Assert.Equal(PanelButtonKind.Move, r.Drag);
            Assert.True(r.JustEngaged);
            Assert.False(r.CurveToggled);
        }

        [Fact]
        public void 押下保持のまま後からhoverしてもengageしない()
        {
            var s = new PanelAdjustState();
            s.Update(true, true, true, PanelButtonKind.None);            // hover 外で押下
            var r = s.Update(true, true, true, PanelButtonKind.Scale);   // 保持したまま hover
            Assert.Equal(PanelButtonKind.None, r.Drag);
        }

        [Fact]
        public void drag中はhoverを外れても継続_releaseで解放()
        {
            var s = new PanelAdjustState();
            s.Update(true, true, true, PanelButtonKind.Move);
            var r = s.Update(true, true, true, PanelButtonKind.None);    // hover 外れ
            Assert.Equal(PanelButtonKind.Move, r.Drag);
            Assert.False(r.JustEngaged);
            r = s.Update(true, true, false, PanelButtonKind.None);       // release
            Assert.Equal(PanelButtonKind.None, r.Drag);
        }

        [Fact]
        public void midDragの無効化と切断は即解放()
        {
            var s = new PanelAdjustState();
            s.Update(true, true, true, PanelButtonKind.Move);
            var r = s.Update(false, true, true, PanelButtonKind.Move);   // config OFF
            Assert.Equal(PanelButtonKind.None, r.Drag);

            s.Clear();
            s.Update(true, true, true, PanelButtonKind.Move);
            r = s.Update(true, false, true, PanelButtonKind.Move);       // snapshot invalid
            Assert.Equal(PanelButtonKind.None, r.Drag);
        }

        [Fact]
        public void 無効中の押下は再有効化フレームで偽risingしない()
        {
            var s = new PanelAdjustState();
            s.Update(false, true, true, PanelButtonKind.Move);           // 無効中に押下
            var r = s.Update(true, true, true, PanelButtonKind.Move);    // 有効化（押しっぱなし）
            Assert.Equal(PanelButtonKind.None, r.Drag);                  // rising でないので engage しない
        }

        [Fact]
        public void 曲面ボタンはrising一回だけトグル発火()
        {
            var s = new PanelAdjustState();
            var r = s.Update(true, true, true, PanelButtonKind.Curve);
            Assert.True(r.CurveToggled);
            Assert.Equal(PanelButtonKind.None, r.Drag);                  // ドラッグ状態には入らない
            r = s.Update(true, true, true, PanelButtonKind.Curve);       // 押しっぱなし
            Assert.False(r.CurveToggled);
        }
    }
}
