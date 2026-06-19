using UnityEngine;

namespace BG2VR.VrFade
{
    /// <summary>
    /// 遷移絵柄 overlay の表示/push 要否の決定（純ロジック）。
    /// 入力は bool/float のみ（MonoBehaviour 非依存＝テスト可能・FadeMirrorPolicy と同型）。
    /// </summary>
    public static class TransitionOverlayPolicy
    {
        // 前回 push 値との差がこれ以下なら push しない（DOTween 毎フレ微小変化の間引き）。
        public const float Epsilon = 0.003f;

        public struct Decision
        {
            public bool ShouldPush;
            public bool Visible;
            public float Alpha;
        }

        /// <summary>
        /// - 非 enabled / 非 active / alpha 0 → 非表示（hide は可視状態から 1 回だけ push）
        /// - 可視状態のエッジ（show/hide 切替）は差分に関わらず必ず push
        /// - 可視継続中は alpha / width / distance の差が ε 超のときのみ push
        /// </summary>
        public static Decision Decide(
            bool featureEnabled, bool transitionActive, float alpha, float width, float distance,
            bool lastVisible, float lastAlpha, float lastWidth, float lastDistance)
        {
            bool wantVisible = featureEnabled && transitionActive && alpha > 0f;
            if (wantVisible != lastVisible)
            {
                return new Decision { ShouldPush = true, Visible = wantVisible, Alpha = alpha };
            }
            if (!wantVisible)
            {
                return new Decision { ShouldPush = false, Visible = false, Alpha = 0f };
            }
            bool delta = Mathf.Abs(alpha - lastAlpha) > Epsilon
                      || Mathf.Abs(width - lastWidth) > Epsilon
                      || Mathf.Abs(distance - lastDistance) > Epsilon;
            return new Decision { ShouldPush = delta, Visible = true, Alpha = alpha };
        }
    }
}
