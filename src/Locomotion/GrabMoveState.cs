namespace BG2VR.Locomotion
{
    /// <summary>grip 移動に関与している手。</summary>
    public enum GripHand { None, Left, Right }

    /// <summary>GrabMoveState の 1 フレーム評価結果。</summary>
    public struct GripStateResult
    {
        public GripHand MoveHand;   // 現在 engage 中の手（DualHold/ResetWait 中は None）
        public bool MarkerResync;   // true=このフレームは移動を適用せず marker を現 pose で取り直す
        public bool ResetNow;       // このフレームでリセットを適用する（dual-hold 1 周期につき 1 回だけ true）
        public bool LeftBusy;       // 左手が grip 関与中（arbiter 除外・ポインタ凍結用）
        public bool RightBusy;
    }

    /// <summary>
    /// grip 移動の純粋状態機械（UnityEngine / BepInEx 非依存・xUnit 可）。
    /// 先勝ち engage / 両手 grip は移動凍結 + 1 秒でリセット発火 / リセット後は両手離すまで再 engage 不可。
    /// spec: docs/superpowers/specs/2026-06-05-bg2-vr-grip-move-locomotion-design.md §4
    /// </summary>
    public sealed class GrabMoveState
    {
        // 固定値（Config 化しない＝ユーザー固定値選好）。
        public const float DualHoldResetSecs = 1.0f;

        private enum Phase { Idle, Engaged, DualHold, ResetWait }

        private Phase _phase;
        private GripHand _hand;   // Engaged/DualHold 中の移動の手（先勝ち）
        private float _dualTimer;

        public GripStateResult Update(bool leftValid, bool leftGrip, bool rightValid, bool rightGrip, float dt)
        {
            bool l = leftValid && leftGrip;
            bool r = rightValid && rightGrip;
            var result = new GripStateResult();

            switch (_phase)
            {
                case Phase.Idle:
                    if (l && r)
                    {
                        // 同一フレームで両手 down → 即 DualHold（リセット意図とみなす）。
                        _phase = Phase.DualHold;
                        _dualTimer = 0f;
                    }
                    else if (l || r)
                    {
                        _phase = Phase.Engaged;
                        _hand = l ? GripHand.Left : GripHand.Right;
                        result.MoveHand = _hand;
                        result.MarkerResync = true;
                    }
                    break;

                case Phase.Engaged:
                {
                    bool moveHeld = (_hand == GripHand.Left) ? l : r;
                    bool otherHeld = (_hand == GripHand.Left) ? r : l;
                    if (moveHeld && otherHeld)
                    {
                        // 両手 grip → 移動凍結 + リセットタイマー開始（リセット操作中のドリフト防止）。
                        _phase = Phase.DualHold;
                        _dualTimer = 0f;
                    }
                    else if (!moveHeld && otherHeld)
                    {
                        // 移動の手だけ離した → 逆手へ引き継ぎ（新規 engage 扱い＝marker 再同期）。
                        _hand = (_hand == GripHand.Left) ? GripHand.Right : GripHand.Left;
                        result.MoveHand = _hand;
                        result.MarkerResync = true;
                    }
                    else if (!moveHeld)
                    {
                        _phase = Phase.Idle;
                        _hand = GripHand.None;
                    }
                    else
                    {
                        result.MoveHand = _hand; // 通常移動フレーム
                    }
                    break;
                }

                case Phase.DualHold:
                    if (l && r)
                    {
                        _dualTimer += dt;
                        if (_dualTimer >= DualHoldResetSecs)
                        {
                            result.ResetNow = true;
                            _phase = Phase.ResetWait;
                            _hand = GripHand.None;
                        }
                    }
                    else if (l || r)
                    {
                        // 1 秒未満で片方離した → 残った手で移動再開（marker 再同期＝ジャンプなし）。
                        // 注: ここは Engaged の「_hand 基準引き継ぎ」と異なり _hand を見ない。
                        // 離した手は grip していない＝動けないため「残った手」の採用が唯一の正解
                        //（spec §4 不変条件: _hand が移動の手を指すのは Idle/Engaged のみ）。
                        _phase = Phase.Engaged;
                        _hand = l ? GripHand.Left : GripHand.Right;
                        result.MoveHand = _hand;
                        result.MarkerResync = true;
                    }
                    else
                    {
                        _phase = Phase.Idle;
                        _hand = GripHand.None;
                    }
                    break;

                case Phase.ResetWait:
                    // リセット直後の誤移動防止: 両手とも離すまで再 engage しない。
                    if (!l && !r)
                    {
                        _phase = Phase.Idle;
                        _hand = GripHand.None;
                    }
                    break;
            }

            // busy = grip を握っていて状態機械が反応している手（Idle へ落ちたフレームは非 busy）。
            result.LeftBusy = l && _phase != Phase.Idle;
            result.RightBusy = r && _phase != Phase.Idle;
            return result;
        }

        public void Clear()
        {
            _phase = Phase.Idle;
            _hand = GripHand.None;
            _dualTimer = 0f;
        }
    }
}
