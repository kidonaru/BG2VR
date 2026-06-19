using UnityEngine;

namespace BG2VR.VrFade
{
    /// <summary>
    /// ゲーム ScreenFade の表示状態 → compositor へ push すべき fade 色の決定（純ロジック）。
    /// 入力は bool/Color/float のみ（MonoBehaviour 非依存＝テスト可能）。
    /// </summary>
    public static class FadeMirrorPolicy
    {
        // 前回 push 値との最大成分差がこれ以下なら push しない（DOTween 毎フレ微小変化の間引き）。
        public const float Epsilon = 0.003f;

        public struct Decision
        {
            public bool ShouldPush;
            public Color Color; // alpha 含む
        }

        /// <summary>
        /// m_image 優先（黒/白フェード本体・色は image の color）→ m_transition（白柄 wipe＝白で近似）
        /// → どちらも非 active なら alpha 0（クリア）。
        /// </summary>
        public static Color EvaluateTarget(bool imageActive, Color imageColor, bool transitionActive, float transitionAlpha)
        {
            if (imageActive) return imageColor;
            if (transitionActive) return new Color(1f, 1f, 1f, transitionAlpha);
            return new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>
        /// 前回 push 値と比較して push 要否を決める。
        /// alpha 0 への遷移（クリア終端）は差が ε 以下でも必ず push する（黒残留＝視界喪失の防止）。
        /// </summary>
        public static Decision Decide(Color target, Color lastPushed)
        {
            bool clearEdge = target.a <= 0f && lastPushed.a > 0f;
            float diff = Mathf.Max(
                Mathf.Max(Mathf.Abs(target.r - lastPushed.r), Mathf.Abs(target.g - lastPushed.g)),
                Mathf.Max(Mathf.Abs(target.b - lastPushed.b), Mathf.Abs(target.a - lastPushed.a)));
            return new Decision { ShouldPush = clearEdge || diff > Epsilon, Color = target };
        }
    }
}
