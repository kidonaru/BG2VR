using UnityEngine;
using BG2VR.VrInput;

namespace BG2VR.KarutaStick
{
    /// <summary>カルタ札の中立識別子（ゲーム enum Karuta.Input 非依存＝テスト可。
    /// patch 側で Karuta.Input へ 1:1 変換する）。</summary>
    internal enum KarutaCard { None, A, B, X, Y, Up, Down, Left, Right }

    /// <summary>
    /// 左右スティックの 4 方向フリック（StickNavMapper の NavState）を 1 枚の札へ解決する純関数（spec §4.3）。
    /// 左スティック=十字札（Up/Down/Left/Right）、右スティック=顔札（↑=Y / ↓=A / ←=X / →=B＝菱形）。
    /// 斜め（縦横同時 Pulse）は倒し量の優勢軸で 1 枚に、両スティック同時は sqrMagnitude の大きい側を採る。
    /// </summary>
    internal static class KarutaStickMap
    {
        public static KarutaCard Resolve(StickNavMapper.NavState left, Vector2 leftStick,
                                         StickNavMapper.NavState right, Vector2 rightStick)
        {
            KarutaCard l = ResolveLeft(left, leftStick);
            KarutaCard r = ResolveRight(right, rightStick);
            if (l != KarutaCard.None && r != KarutaCard.None)
                return rightStick.sqrMagnitude > leftStick.sqrMagnitude ? r : l;
            return l != KarutaCard.None ? l : r;
        }

        // 縦横同時 Pulse のとき縦を優先するか（|y| >= |x|）。Up と Down は同時に立たない（StickNavMapper 仕様）。
        private static bool PreferVertical(StickNavMapper.NavState s, Vector2 v)
        {
            bool vert = s.UpPulse || s.DownPulse;
            bool horiz = s.LeftPulse || s.RightPulse;
            return vert && (!horiz || Mathf.Abs(v.y) >= Mathf.Abs(v.x));
        }

        private static KarutaCard ResolveLeft(StickNavMapper.NavState s, Vector2 v)
        {
            if (!(s.UpPulse || s.DownPulse || s.LeftPulse || s.RightPulse)) return KarutaCard.None;
            if (PreferVertical(s, v)) return s.UpPulse ? KarutaCard.Up : KarutaCard.Down;
            return s.LeftPulse ? KarutaCard.Left : KarutaCard.Right;
        }

        private static KarutaCard ResolveRight(StickNavMapper.NavState s, Vector2 v)
        {
            if (!(s.UpPulse || s.DownPulse || s.LeftPulse || s.RightPulse)) return KarutaCard.None;
            if (PreferVertical(s, v)) return s.UpPulse ? KarutaCard.Y : KarutaCard.A;
            return s.LeftPulse ? KarutaCard.X : KarutaCard.B;
        }
    }
}
