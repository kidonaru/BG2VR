using UnityEngine;
using Xunit;
using BG2VR.WorldUi;

namespace BG2VR.Tests
{
    public class ButtonBarLayoutTests
    {
        private const float W = 2f, H = 1.125f;
        private const float D = 1.5f;       // 視点距離(m)
        private const float Ratio = 0.072f; // 視角比率（テスト固定値＝旧既定。yaml 既定は実機チューニングで変更されるため独立）

        [Fact]
        public void ボタンは3つ中央対称でパネル下端より下()
        {
            float side = ButtonBarLayout.ButtonSide(D, Ratio, 0f);
            var c0 = ButtonBarLayout.ButtonCenter(0, W, H, side);
            var c1 = ButtonBarLayout.ButtonCenter(1, W, H, side);
            var c2 = ButtonBarLayout.ButtonCenter(2, W, H, side);
            Assert.Equal(0f, c1.x, 4);                       // 中央
            Assert.Equal(-c0.x, c2.x, 4);                    // 左右対称
            Assert.True(c0.x < c1.x && c1.x < c2.x);
            foreach (var c in new[] { c0, c1, c2 })
            {
                Assert.Equal(c0.y, c.y, 4);                  // 同じ高さ
                Assert.Equal(0f, c.z, 4);                    // z=0 平面
                Assert.True(c.y + side * 0.5f < -H * 0.5f);  // 上端がパネル下端より下
            }
        }

        [Fact]
        public void ボタン辺は距離と比率に比例()
        {
            // 視角一定: 距離比例（clamp なし）
            Assert.Equal(1.5f * Ratio, ButtonBarLayout.ButtonSide(1.5f, Ratio, 0f), 5);
            Assert.Equal(ButtonBarLayout.ButtonSide(1f, Ratio, 0f) * 2f, ButtonBarLayout.ButtonSide(2f, Ratio, 0f), 5);
            // 比率比例（config 調整軸）
            Assert.Equal(ButtonBarLayout.ButtonSide(1.5f, 0.05f, 0f) * 2f, ButtonBarLayout.ButtonSide(1.5f, 0.1f, 0f), 5);
        }

        [Fact]
        public void 加算値は距離に依らず一定で効き0未満はfloor()
        {
            // 加算は距離スケールの外（どの距離でも +0.02m）
            Assert.Equal(ButtonBarLayout.ButtonSide(1.5f, Ratio, 0f) + 0.02f,
                ButtonBarLayout.ButtonSide(1.5f, Ratio, 0.02f), 5);
            Assert.Equal(ButtonBarLayout.ButtonSide(4f, Ratio, 0f) + 0.02f,
                ButtonBarLayout.ButtonSide(4f, Ratio, 0.02f), 5);
            // 負の加算で 0 未満になる組合せは 0 に floor（負スケール quad の反転防止）
            Assert.Equal(0f, ButtonBarLayout.ButtonSide(0.5f, Ratio, -0.1f), 5);
        }

        [Fact]
        public void 旧既定構成で旧サイズと同値_式の回帰pin()
        {
            // 旧実装: 幅 1.2m × 9% = 0.108m。比率 0.072 × 距離 1.5m + 加算 0 = 0.108m（式の回帰検出用）。
            // 現 yaml 既定は実機チューニングで 0.02 / 0.04m に変更済み（見た目の互換は意図的に放棄）。
            Assert.Equal(0.108f, ButtonBarLayout.ButtonSide(1.5f, Ratio, 0f), 4);
        }

        [Fact]
        public void 拡張矩形はパネルと全ボタンを包含()
        {
            float side = ButtonBarLayout.ButtonSide(D, Ratio, 0f);
            ButtonBarLayout.ExpandedRect(W, H, side, out Vector3 center, out float hw, out float hh);
            // パネル四隅
            Assert.True(Mathf.Abs(W * 0.5f - center.x) <= hw);
            Assert.True(Mathf.Abs(H * 0.5f - center.y) <= hh);
            Assert.True(Mathf.Abs(-H * 0.5f - center.y) <= hh);
            // ボタン下端・左右端
            var c0 = ButtonBarLayout.ButtonCenter(0, W, H, side);
            Assert.True(Mathf.Abs(c0.y - side * 0.5f - center.y) <= hh);
            Assert.True(Mathf.Abs(c0.x - side * 0.5f - center.x) <= hw);
        }
    }
}
