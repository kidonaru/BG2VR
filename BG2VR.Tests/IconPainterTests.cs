using Xunit;
using BG2VR.WorldUi;

namespace BG2VR.Tests
{
    public class IconPainterTests
    {
        [Theory]
        [InlineData(PanelButtonKind.Move)]
        [InlineData(PanelButtonKind.Scale)]
        [InlineData(PanelButtonKind.Curve)]
        public void アイコンはサイズ正で背景と前景の2色以上を含む(PanelButtonKind kind)
        {
            var px = IconPainter.Paint(kind);
            Assert.Equal(IconPainter.Size * IconPainter.Size, px.Length);
            int fg = 0, bg = 0;
            foreach (var p in px)
            {
                if (p.r > 200) fg++;
                else bg++;
            }
            Assert.True(fg > 20, $"前景 {fg}px");   // 描画されている
            Assert.True(bg > fg, "背景が前景より多い"); // 塗りつぶしバグでない
        }
    }
}
