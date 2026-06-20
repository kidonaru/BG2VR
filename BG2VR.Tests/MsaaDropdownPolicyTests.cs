using BG2VR.Patches.Settings;
using Xunit;

namespace BG2VR.Tests
{
    public class MsaaDropdownPolicyTests
    {
        // Values は dropdown のラベル順（オフ/2x/4x/8x）と一致する有効 MSAA 値
        [Fact]
        public void Values_は1248の昇順()
        {
            Assert.Equal(new[] { 1, 2, 4, 8 }, MsaaDropdownPolicy.Values);
        }

        // --- ValueFromIndex: dropdown 選択 index → MSAA 値 ---
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(3, 8)]
        public void ValueFromIndex_正常(int idx, int expected)
            => Assert.Equal(expected, MsaaDropdownPolicy.ValueFromIndex(idx));

        [Theory]
        [InlineData(-1, 1)]   // 下クランプ
        [InlineData(99, 8)]   // 上クランプ
        public void ValueFromIndex_範囲外はクランプ(int idx, int expected)
            => Assert.Equal(expected, MsaaDropdownPolicy.ValueFromIndex(idx));

        // --- IndexFromValue: 現在値 → dropdown index（非有効値は下方向丸めで最寄り有効値の index）---
        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(4, 2)]
        [InlineData(8, 3)]
        [InlineData(3, 1)]    // 3→2x の index
        [InlineData(5, 2)]    // 5→4x の index
        [InlineData(0, 0)]    // 0→1(オフ)
        [InlineData(-5, 0)]   // 負→オフ
        [InlineData(16, 3)]   // 上限超→8x
        public void IndexFromValue_丸め(int value, int expectedIdx)
            => Assert.Equal(expectedIdx, MsaaDropdownPolicy.IndexFromValue(value));

        // round-trip: 全 index は value→index で自分自身へ戻る
        [Fact]
        public void RoundTrip_index_value_index()
        {
            for (int i = 0; i < MsaaDropdownPolicy.Values.Length; i++)
                Assert.Equal(i, MsaaDropdownPolicy.IndexFromValue(MsaaDropdownPolicy.ValueFromIndex(i)));
        }

        // --- Sanitize: 任意 int → 有効 MSAA 値 {1,2,4,8}（下方向丸め）。URP msaaSampleCount 設定用 ---
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(3, 2)]
        [InlineData(4, 4)]
        [InlineData(6, 4)]
        [InlineData(8, 8)]
        [InlineData(99, 8)]
        [InlineData(-5, 1)]
        public void Sanitize_最寄り有効値へ下方向丸め(int v, int expected)
            => Assert.Equal(expected, MsaaDropdownPolicy.Sanitize(v));
    }
}
