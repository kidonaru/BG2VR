using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>指ベンドの純ロジック（Mathf のみ・native ECall 非依存＝テスト可能）。
    /// curl 値の dt 補正 EMA 平滑と、curl01→曲げ角(度)の算出を担う。
    /// 角→Quaternion（Quaternion.AngleAxis）と rest 合成は native ECall のため HandFingerPoser（実機）側で行う
    /// （ControllerModelPose が Quaternion.Euler を Runner 側に置くのと同じ制約）。</summary>
    internal static class FingerCurlMath
    {
        /// <summary>dt 補正付き EMA（フレームレート非依存・RaySmoother と同方式）。
        /// tau ≦ 0 はパススルー（target をそのまま返す＝τ を下げた直後に古い値へ引っ張られない）。</summary>
        public static float Smooth(float prev, float target, float dt, float tau)
        {
            if (tau <= 0f) return target;
            float alpha = 1f - Mathf.Exp(-dt / tau);
            return Mathf.Lerp(prev, target, alpha);
        }

        /// <summary>curl 入力(0-1)→各関節の曲げ角(度)。入力0で initialDeg・入力1で maxDeg、その間を線形補間
        /// （入力は [0,1] にクランプ）。relaxed な初期姿勢から押し込みで最大角まで曲げる。</summary>
        public static float CurlAngle(float curl01, float initialDeg, float maxDeg)
        {
            return Mathf.Lerp(initialDeg, maxDeg, Mathf.Clamp01(curl01));
        }
    }
}
