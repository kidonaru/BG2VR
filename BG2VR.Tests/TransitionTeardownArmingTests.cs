using BG2VR.TransitionGuard;
using Xunit;

namespace BG2VR.Tests
{
    public class TransitionTeardownArmingTests
    {
        [Fact]
        public void 未armならconsumeはfalse_noop()
        {
            var a = new TransitionTeardownArming();
            Assert.False(a.IsArmed);
            // env 切替等の LoadSceneAsync は arm されていない＝消費しない
            Assert.False(a.TryConsume());
            Assert.False(a.TryConsume());
        }

        [Fact]
        public void arm後の最初のconsumeのみtrue_2回目はfalse()
        {
            var a = new TransitionTeardownArming();
            a.Arm();
            Assert.True(a.IsArmed);
            // 遷移内の最初のシーンロードで 1 回だけ teardown
            Assert.True(a.TryConsume());
            Assert.False(a.IsArmed);
            // 同一遷移内の後続シーンロードは consume しない（二重 teardown 防止）
            Assert.False(a.TryConsume());
        }

        [Fact]
        public void consumeがarmを必ず消す_stale化しない()
        {
            var a = new TransitionTeardownArming();
            a.Arm();
            Assert.True(a.TryConsume());
            // arm が残らない＝次の無関係な env 切替 load を誤 consume しない
            Assert.False(a.IsArmed);
            Assert.False(a.TryConsume());
        }

        [Fact]
        public void 中断遷移のarmは次の無関係loadに食われる_既知の限界を固定()
        {
            // arm 後その遷移が LoadSceneAsync 到達前に中断（cancel/例外）すると _armed が残り、
            // 次に来た無関係な load（env 切替等）が 1 回誤 consume する＝既知の限界（実害=余分な teardown 1 回）。
            // 将来 Reset 経路を足す際の回帰検出点として、この挙動を仕様として固定しておく。
            var a = new TransitionTeardownArming();
            a.Arm();
            // この遷移では consume されないまま中断 → 次に来た無関係 load が armed を食う
            Assert.True(a.TryConsume());
            Assert.False(a.IsArmed);
        }

        [Fact]
        public void 再armは上書き_直近遷移が支配()
        {
            var a = new TransitionTeardownArming();
            a.Arm();
            a.Arm(); // ネスト/多重遷移開始
            Assert.True(a.IsArmed);
            Assert.True(a.TryConsume()); // 1 回の teardown に集約
            Assert.False(a.TryConsume());
        }
    }
}
