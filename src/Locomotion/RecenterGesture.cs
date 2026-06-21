namespace BG2VR.Locomotion
{
    /// <summary>
    /// 両手 Grip 長押し → 正面リセット 1 回発火の純粋状態機械（UnityEngine / BepInEx 非依存・xUnit 可）。
    /// 両手とも valid かつ grip を HoldSecs 継続したら 1 回だけ true。fire 後は両手が離れるまで再武装しない。
    /// HoldSecs は GrabMoveState.DualHoldResetSecs と同値＝既存「移動量リセット」と同じ長押しで統合発火する。
    /// spec: docs/superpowers/specs/2026-06-21-bg2vr-vr-recenter-design.md §5.2 §7.2
    /// </summary>
    public sealed class RecenterGesture
    {
        public const float HoldSecs = 1.0f;

        private enum Phase { Idle, Holding, Fired }

        private Phase _phase;
        private float _timer;

        /// <summary>1 フレーム評価。両手 grip を HoldSecs 継続したフレームで 1 回だけ true。</summary>
        public bool Update(bool leftValid, bool leftGrip, bool rightValid, bool rightGrip, float dt)
        {
            bool both = leftValid && leftGrip && rightValid && rightGrip;

            switch (_phase)
            {
                case Phase.Idle:
                    if (both) { _phase = Phase.Holding; _timer = 0f; }
                    break;

                case Phase.Holding:
                    if (!both) { _phase = Phase.Idle; _timer = 0f; break; }
                    _timer += dt;
                    if (_timer >= HoldSecs) { _phase = Phase.Fired; return true; }
                    break;

                case Phase.Fired:
                    // 連続発火防止: 両手とも grip を離すまで再武装しない（片手保持では Fired 継続）。
                    if (!leftGrip && !rightGrip) { _phase = Phase.Idle; _timer = 0f; }
                    break;
            }
            return false;
        }

        public void Clear() { _phase = Phase.Idle; _timer = 0f; }
    }
}
