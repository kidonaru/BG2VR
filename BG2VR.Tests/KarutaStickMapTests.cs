using UnityEngine;
using Xunit;
using BG2VR.KarutaStick;
using BG2VR.VrInput;

namespace BG2VR.Tests
{
    public class KarutaStickMapTests
    {
        private static StickNavMapper.NavState NS(bool up, bool down, bool left, bool right)
            => new StickNavMapper.NavState { UpPulse = up, DownPulse = down, LeftPulse = left, RightPulse = right };
        private static readonly StickNavMapper.NavState Empty = NS(false, false, false, false);

        // 左スティック 4 方向 → 十字札
        [Fact] public void LeftUp_ToUp()
            => Assert.Equal(KarutaCard.Up, KarutaStickMap.Resolve(NS(true, false, false, false), new Vector2(0, 1), Empty, Vector2.zero));
        [Fact] public void LeftDown_ToDown()
            => Assert.Equal(KarutaCard.Down, KarutaStickMap.Resolve(NS(false, true, false, false), new Vector2(0, -1), Empty, Vector2.zero));
        [Fact] public void LeftLeft_ToLeft()
            => Assert.Equal(KarutaCard.Left, KarutaStickMap.Resolve(NS(false, false, true, false), new Vector2(-1, 0), Empty, Vector2.zero));
        [Fact] public void LeftRight_ToRight()
            => Assert.Equal(KarutaCard.Right, KarutaStickMap.Resolve(NS(false, false, false, true), new Vector2(1, 0), Empty, Vector2.zero));

        // 右スティック 4 方向 → 顔札（菱形 ↑=Y ↓=A ←=X →=B）
        [Fact] public void RightUp_ToY()
            => Assert.Equal(KarutaCard.Y, KarutaStickMap.Resolve(Empty, Vector2.zero, NS(true, false, false, false), new Vector2(0, 1)));
        [Fact] public void RightDown_ToA()
            => Assert.Equal(KarutaCard.A, KarutaStickMap.Resolve(Empty, Vector2.zero, NS(false, true, false, false), new Vector2(0, -1)));
        [Fact] public void RightLeft_ToX()
            => Assert.Equal(KarutaCard.X, KarutaStickMap.Resolve(Empty, Vector2.zero, NS(false, false, true, false), new Vector2(-1, 0)));
        [Fact] public void RightRight_ToB()
            => Assert.Equal(KarutaCard.B, KarutaStickMap.Resolve(Empty, Vector2.zero, NS(false, false, false, true), new Vector2(1, 0)));

        // Pulse 無し → None
        [Fact] public void NoPulse_ToNone()
            => Assert.Equal(KarutaCard.None, KarutaStickMap.Resolve(Empty, Vector2.zero, Empty, Vector2.zero));

        // 斜め（右↗）: |y|>|x| → 縦優先(Y) / |x|>|y| → 横優先(B)
        [Fact] public void RightDiagonal_VertDominant_ToY()
            => Assert.Equal(KarutaCard.Y, KarutaStickMap.Resolve(Empty, Vector2.zero, NS(true, false, false, true), new Vector2(0.5f, 0.7f)));
        [Fact] public void RightDiagonal_HorizDominant_ToB()
            => Assert.Equal(KarutaCard.B, KarutaStickMap.Resolve(Empty, Vector2.zero, NS(true, false, false, true), new Vector2(0.7f, 0.5f)));

        // 両スティック同時 → 倒し量(sqrMagnitude)の大きい側
        [Fact] public void BothSticks_RightLarger_PicksRight()
            => Assert.Equal(KarutaCard.A, KarutaStickMap.Resolve(NS(true, false, false, false), new Vector2(0, 0.6f), NS(false, true, false, false), new Vector2(0, -0.9f)));
        [Fact] public void BothSticks_LeftLarger_PicksLeft()
            => Assert.Equal(KarutaCard.Up, KarutaStickMap.Resolve(NS(true, false, false, false), new Vector2(0, 0.9f), NS(false, true, false, false), new Vector2(0, -0.5f)));

        // 両スティック同値（sqrMagnitude タイ）→ 左優先（Resolve は `>` 比較＝右が厳密に大きいときのみ右）
        [Fact] public void BothSticks_EqualMagnitude_PicksLeft()
            => Assert.Equal(KarutaCard.Up, KarutaStickMap.Resolve(NS(true, false, false, false), new Vector2(0, 0.8f), NS(false, true, false, false), new Vector2(0, -0.8f)));
    }
}
