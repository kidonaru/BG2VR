using UnityEngine;
using Xunit;
using BG2VR.VrInput;

namespace BG2VR.Tests
{
    public class SettingsLaserMathTests
    {
        // full 矩形は de-risk 実測値（原点 0,0・幅 1476.92・高 830.77）を使う。
        private static readonly Rect Full = new Rect(0f, 0f, 1476.92f, 830.77f);

        [Fact]
        public void UvToPanelPoint_左下uvはpanel下端_Y反転()
        {
            // uv(0,0)=RT 左下 → panel(x=0, y=height)（panel は Y 上原点）。
            var p = SettingsLaserMath.UvToPanelPoint(new Vector2(0f, 0f), Full);
            Assert.Equal(0f, p.x, 3);
            Assert.Equal(Full.height, p.y, 2);
        }

        [Fact]
        public void UvToPanelPoint_左上uvはpanel原点()
        {
            // uv(0,1)=RT 左上 → panel(0,0)。
            var p = SettingsLaserMath.UvToPanelPoint(new Vector2(0f, 1f), Full);
            Assert.Equal(0f, p.x, 3);
            Assert.Equal(0f, p.y, 3);
        }

        [Fact]
        public void UvToPanelPoint_右中央()
        {
            var p = SettingsLaserMath.UvToPanelPoint(new Vector2(1f, 0.5f), Full);
            Assert.Equal(Full.width, p.x, 2);
            Assert.Equal(Full.height * 0.5f, p.y, 2);
        }

        [Fact]
        public void UvToPanelPoint_非原点worldBoundのoffsetが加算される()
        {
            // visualTree が原点でない場合の offset 項（full.x/full.y）の正しさを検証。
            var off = new Rect(10f, 20f, 200f, 100f);
            var p = SettingsLaserMath.UvToPanelPoint(new Vector2(0.5f, 1f), off);
            Assert.Equal(10f + 0.5f * 200f, p.x, 3); // 110
            Assert.Equal(20f + 0f, p.y, 3);          // uv.y=1 → 上端 = full.y
            var q = SettingsLaserMath.UvToPanelPoint(new Vector2(0f, 0f), off);
            Assert.Equal(10f, q.x, 3);
            Assert.Equal(20f + 100f, q.y, 3);        // uv.y=0 → 下端 = full.y + height
        }

        [Fact]
        public void PanelXToNormalized_両端とクランプ()
        {
            // track: xMin=100, width=300 → [100,400]
            Assert.Equal(0f, SettingsLaserMath.PanelXToNormalized(100f, 100f, 300f), 4);
            Assert.Equal(1f, SettingsLaserMath.PanelXToNormalized(400f, 100f, 300f), 4);
            Assert.Equal(0.5f, SettingsLaserMath.PanelXToNormalized(250f, 100f, 300f), 4);
            Assert.Equal(0f, SettingsLaserMath.PanelXToNormalized(50f, 100f, 300f), 4);  // 左クランプ
            Assert.Equal(1f, SettingsLaserMath.PanelXToNormalized(999f, 100f, 300f), 4); // 右クランプ
        }

        [Fact]
        public void PanelXToNormalized_幅ゼロは0()
        {
            Assert.Equal(0f, SettingsLaserMath.PanelXToNormalized(250f, 100f, 0f), 4);
        }
    }
}
