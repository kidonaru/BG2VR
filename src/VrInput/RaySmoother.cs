using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// ポインタ ray（origin/方向）の EMA スムージング（純ロジック・Vector3/Mathf のみ）。
    /// Quaternion を使わず Vector3 で閉じる（native ECall 回避＝テスト可能。微小角では slerp と同等）。
    /// </summary>
    public sealed class RaySmoother
    {
        private bool m_initialized;
        private Vector3 m_origin;
        private Vector3 m_dir;

        /// <summary>
        /// tau ≦ 0 はパススルー（状態も raw に追従させ、τ を後から上げても古い値に引っ張られない）。
        /// 初回は raw をそのまま採用（ゼロ初期値からの引っ張り防止）。
        /// </summary>
        public void Update(Vector3 rawOrigin, Vector3 rawDir, float dt, float tau, out Vector3 origin, out Vector3 dir)
        {
            if (tau <= 0f || !m_initialized)
            {
                m_origin = rawOrigin;
                m_dir = rawDir.normalized;
                m_initialized = true;
            }
            else
            {
                // dt 補正付き EMA（フレームレート非依存）: alpha = 1 - e^(-dt/τ)
                float alpha = 1f - Mathf.Exp(-dt / tau);
                m_origin = Vector3.Lerp(m_origin, rawOrigin, alpha);
                Vector3 d = Vector3.Lerp(m_dir, rawDir, alpha);
                // 退化（ほぼ反対向き同士の lerp でゼロ化）時は raw を採用。
                m_dir = d.sqrMagnitude > 1e-12f ? d.normalized : rawDir.normalized;
            }
            origin = m_origin;
            dir = m_dir;
        }

        public void Reset() => m_initialized = false;
    }
}
