using GB;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrFade
{
    /// <summary>
    /// ゲームの ScreenFade(GBSystem.m_fade) を VR compositor fade へ毎フレミラーする常駐 runner。
    /// rig 非依存（BG2VR_Runtime 上）＝遷移 rig teardown 中もミラー継続（session レベル fade の核心価値）。
    /// 注意: VRModCore.IsVrActive は遷移 teardown 中 false になるためガードに使わない
    /// （session 生死は fork facade の IsVrAvailable 自己ガードに委ねる。spec §6）。
    /// 対象は m_fade のみ（m_bgFade=背景演出 / CrossFade=静止画クロスは対象外・spec §2）。
    /// </summary>
    internal sealed class VrFadeRunner : MonoBehaviour
    {
        /// <summary>
        /// 固定位置シーンの遷移後フェード保持要求（ScenePinnedPoseRunner が駆動）。
        /// true の間は target を不透明黒に上書きする。push 経路は通常どおりなので m_lastPushed の
        /// 整合が保たれ、解除時にゲーム fade（通常は clear）へ自動復帰して黒が明ける。
        /// EnableVrFade=OFF / fade 不在時は通常どおり保持を無視（黒の押し付けはしない）。
        /// </summary>
        public static bool HoldBlack;

        // 直近で compositor へ push できた色。push 失敗（session 非生存）時は更新しない＝
        // session 復帰後に差分検知で自動再 push される（VR ON の瞬間に既にフェード中でも途中参加できる）。
        private Color m_lastPushed = new Color(0f, 0f, 0f, 0f);

        private void Update()
        {
            if (!global::BG2VR.Configs.EnableVrFade.Value)
            {
                ClearIfPushed();
                return;
            }

            ScreenFade fade = GBSystem.Instance != null ? GBSystem.Instance.m_fade : null;
            if (fade == null)
            {
                ClearIfPushed();
                return;
            }

            // activeInHierarchy: ScreenFade root ごと非 active のケースも拾う（activeSelf では不足）。
            bool imageActive = fade.m_image != null && fade.m_image.gameObject.activeInHierarchy;
            Color imageColor = imageActive ? fade.m_image.color : default;
            bool transitionActive = fade.m_transition != null && fade.m_transition.gameObject.activeInHierarchy;
            float transitionAlpha = transitionActive ? fade.m_transition.color.a : 0f;

            Color target = FadeMirrorPolicy.EvaluateTarget(imageActive, imageColor, transitionActive, transitionAlpha);
            // 固定位置シーンの遷移後フェード保持: 適用が安定するまで黒を維持してカメラ収束のチラつきを隠す。
            if (HoldBlack) target = new Color(0f, 0f, 0f, 1f);
            Push(FadeMirrorPolicy.Decide(target, m_lastPushed));
        }

        private void OnDestroy() => ClearIfPushed();

        // 残留黒で視界喪失しないよう、push 済み fade のクリアを保証する（config OFF / destroy 時）。
        private void ClearIfPushed()
        {
            if (m_lastPushed.a <= 0f) return;
            Push(new FadeMirrorPolicy.Decision { ShouldPush = true, Color = new Color(0f, 0f, 0f, 0f) });
        }

        private void Push(FadeMirrorPolicy.Decision d)
        {
            if (!d.ShouldPush) return;
            if (VRModCore.SetCompositorFade(d.Color.r, d.Color.g, d.Color.b, d.Color.a))
                m_lastPushed = d.Color;
        }
    }
}
