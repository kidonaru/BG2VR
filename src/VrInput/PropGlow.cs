using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// 色駆動プロップ（サイリウム）の「発光させる submesh」を彩度で判定し、HDR emission 色を作る純関数。
    /// 色名ハードコードを避け、彩度（saturation）しきい値で「鮮やかな部分＝発光部」を選ぶ。
    /// UnityEngine のみ依存（BepInEx/Harmony 非参照）＝xUnit 単体テスト対象。
    /// 発光判定は呼出し側で kind==GlowStick に scope する（タンバリンの金/茶も彩度が高く誤発光するため）。
    /// </summary>
    internal static class PropGlow
    {
        // 黒（max≈0）での 0 除算→NaN を避けるガード閾値（構造定数）。
        private const float Epsilon = 1e-4f;

        /// <summary>HSV 彩度 = (max-min)/max。max≈0（黒）のときは 0 を返す（0 除算ガード）。</summary>
        public static float Saturation(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max <= Epsilon) return 0f; // 黒は彩度 0（NaN 防止）
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return (max - min) / max;
        }

        /// <summary>彩度がしきい値以上なら発光部とみなす（「ピンク部分」判定・色名非依存）。</summary>
        public static bool IsGlowing(Color baseColor, float satThr)
        {
            return Saturation(baseColor) >= satThr;
        }

        /// <summary>素色 × strength の HDR emission 色（alpha は 1 固定）。strength>1 で内部 HDR バッファ経由 Bloom に届く。
        /// 素色（brightness 補正前の MatBaseColor）から作る＝lit 明るさと独立に発光強度を制御する意図。</summary>
        public static Color EmissionColor(Color baseColor, float strength)
        {
            return new Color(baseColor.r * strength, baseColor.g * strength, baseColor.b * strength, 1f);
        }
    }
}
