using UnityEngine;

namespace BG2VR.Locomotion
{
    /// <summary>
    /// grip 移動の手 pose（位置 + 回転）の EMA スムージング（純ロジック・xUnit 可）。
    /// 回転は成分 lerp + 正規化（nlerp 相当。フレーム間の微小角差では slerp と同等）で、
    /// 半球補正（dot&lt;0 で raw を符号反転）により quaternion の二重被覆（q ≡ -q）にも連続。
    /// Quaternion.Slerp/Normalize は native ECall でテストホスト不在のため成分直計算で書く。
    /// </summary>
    public sealed class GripPoseSmoother
    {
        private bool m_initialized;
        private Vector3 m_pos;
        private Quaternion m_rot;

        /// <summary>
        /// tau ≦ 0 はパススルー（状態も raw に追従させ、τ を後から上げても古い値に引っ張られない）。
        /// 初回は raw をそのまま採用（ゼロ初期値からの引っ張り防止）。
        /// </summary>
        public void Update(Vector3 rawPos, Quaternion rawRot, float dt, float tau,
            out Vector3 pos, out Quaternion rot)
        {
            if (tau <= 0f || !m_initialized)
            {
                m_pos = rawPos;
                m_rot = NormalizeOr(rawRot, rawRot);
                m_initialized = true;
            }
            else
            {
                // dt 補正付き EMA（フレームレート非依存）: alpha = 1 - e^(-dt/τ)
                float alpha = 1f - Mathf.Exp(-dt / tau);
                m_pos = Vector3.Lerp(m_pos, rawPos, alpha);

                // 半球補正: q と −q は同一回転。近い側へ lerp しないと EMA が大回りして暴れる。
                float dot = m_rot.x * rawRot.x + m_rot.y * rawRot.y + m_rot.z * rawRot.z + m_rot.w * rawRot.w;
                float sign = dot < 0f ? -1f : 1f;
                var q = new Quaternion(
                    m_rot.x + (rawRot.x * sign - m_rot.x) * alpha,
                    m_rot.y + (rawRot.y * sign - m_rot.y) * alpha,
                    m_rot.z + (rawRot.z * sign - m_rot.z) * alpha,
                    m_rot.w + (rawRot.w * sign - m_rot.w) * alpha);
                m_rot = NormalizeOr(q, rawRot);
            }
            pos = m_pos;
            rot = m_rot;
        }

        public void Reset() => m_initialized = false;

        // 成分正規化。退化（ほぼ直交する quat 同士の lerp でゼロ化）時は raw を採用。
        private static Quaternion NormalizeOr(Quaternion q, Quaternion fallback)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-6f) return fallback;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }
    }
}
