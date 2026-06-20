using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// 拡大ボタンドラッグの数学（純関数）。engage 時のレーザーピッチとサイズを基準に、
    /// 現在ピッチとの差を指数マッピングで新サイズへ解く（上=拡大・乗算的＝感覚線形）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-adjust-buttons-design.md §6
    /// </summary>
    public static class ScaleDragSolver
    {
        // 倍率 = e^(Rate·Δpitch)。Δ±30°(0.52rad) ≈ ×3.7/÷3.7 で WorldUiSize range を片振りでカバー。
        public const float Rate = 2.5f;

        /// <summary>engage 基準（サイズ・ピッチ rad）と現在ピッチから新サイズを解く。</summary>
        public static float Solve(float engageSize, float engagePitch, float currentPitch, float min, float max)
            => Mathf.Clamp(engageSize * Mathf.Exp(Rate * (currentPitch - engagePitch)), min, max);

        /// <summary>方向ベクトルからピッチ角(rad)。退化（ゼロベクトル）は 0。</summary>
        public static float Pitch(Vector3 dir)
        {
            float m = dir.magnitude;
            if (m < 1e-6f) return 0f;
            return Mathf.Asin(Mathf.Clamp(dir.y / m, -1f, 1f));
        }
    }
}
