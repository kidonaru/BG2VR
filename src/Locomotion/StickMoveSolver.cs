using UnityEngine;

namespace BG2VR.Locomotion
{
    /// <summary>
    /// grip+スティック 平行移動の数学（純関数・xUnit 可・UnityEngine の Vector2/3 と Mathf のみ）。
    /// 頭基準・水平のみ: stick.y=頭の水平 forward、stick.x=頭の水平 right（ストレイフ）。上下移動なし。
    /// headForward/Right は呼び出し側（ProjectorRunner）が水平投影 + 正規化して渡す
    /// （eye 不在時は zero ベクトル＝Δ=0）。
    /// </summary>
    public static class StickMoveSolver
    {
        /// <summary>1 フレームの world 平行移動量。stick 大きさが deadzone 未満なら zero。</summary>
        public static Vector3 ComputeDelta(
            Vector2 stick, Vector3 headForwardHoriz, Vector3 headRightHoriz,
            float speed, float deadzone, float dt)
        {
            float mag = stick.magnitude;
            if (mag < deadzone || mag < 1e-6f) return Vector3.zero;
            Vector3 dir = headRightHoriz * stick.x + headForwardHoriz * stick.y;
            return dir * (speed * dt);
        }
    }
}
