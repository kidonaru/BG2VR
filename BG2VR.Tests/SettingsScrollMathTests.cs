using Xunit;
using BG2VR.VrInput;

namespace BG2VR.Tests
{
    public class SettingsScrollMathTests
    {
        private const float Deadzone = 0.15f;
        private const float Speed = 1200f;
        private const float Dt = 0.016f;

        [Fact]
        public void Delta_deadzone内は0_正側()
        {
            // |stickY| < deadzone はドリフト無視＝0。
            Assert.Equal(0f, SettingsScrollMath.Delta(0.1f, Deadzone, Speed, Dt));
        }

        [Fact]
        public void Delta_deadzone内は0_負側()
        {
            Assert.Equal(0f, SettingsScrollMath.Delta(-0.1f, Deadzone, Speed, Dt));
        }

        [Fact]
        public void Delta_ゼロ入力は0()
        {
            Assert.Equal(0f, SettingsScrollMath.Delta(0f, Deadzone, Speed, Dt));
        }

        [Fact]
        public void Delta_上倒しは負_上スクロール()
        {
            // 上倒し(stickY>0)=ホイール上＝scrollOffset を減らす方向＝負。
            Assert.True(SettingsScrollMath.Delta(0.5f, Deadzone, Speed, Dt) < 0f);
        }

        [Fact]
        public void Delta_下倒しは正_下スクロール()
        {
            // 下倒し(stickY<0)=ホイール下＝scrollOffset を増やす方向＝正。
            Assert.True(SettingsScrollMath.Delta(-0.5f, Deadzone, Speed, Dt) > 0f);
        }

        [Fact]
        public void Delta_速度に比例する()
        {
            // speed 2 倍で delta も 2 倍（dt・stickY 固定）。
            float a = SettingsScrollMath.Delta(0.5f, Deadzone, Speed, Dt);
            float b = SettingsScrollMath.Delta(0.5f, Deadzone, Speed * 2f, Dt);
            Assert.Equal(a * 2f, b, 4);
        }

        [Fact]
        public void Delta_dtに比例する()
        {
            // dt 2 倍で delta も 2 倍（フレーム時間に対し一定速度）。
            float a = SettingsScrollMath.Delta(0.5f, Deadzone, Speed, Dt);
            float b = SettingsScrollMath.Delta(0.5f, Deadzone, Speed, Dt * 2f);
            Assert.Equal(a * 2f, b, 4);
        }

        [Fact]
        public void Delta_deadzone境界ちょうどは非ゼロ()
        {
            // stickY == deadzone は「未満」でない＝非ゼロ（境界は入力扱い）。
            Assert.True(SettingsScrollMath.Delta(Deadzone, Deadzone, Speed, Dt) < 0f);
        }

        [Fact]
        public void Delta_既定値での1フレーム量_概算()
        {
            // stickY=1.0（フル倒し）・既定 speed1200・dt0.016 ＝ -19.2px/フレーム。
            Assert.Equal(-1.0f * Speed * Dt, SettingsScrollMath.Delta(1.0f, Deadzone, Speed, Dt), 4);
        }
    }
}
