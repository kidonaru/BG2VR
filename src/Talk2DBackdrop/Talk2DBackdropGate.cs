using GB;
using GB.Scene;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.Talk2DBackdrop
{
    /// <summary>
    /// Talk2D backdrop を適用すべきフレームかの共有判定（非純粋＝GBSystem/Configs/VRModCore 参照）。
    /// Talk2DBackdropRunner（背景押し出し）と EyeCullingCoordinator（暗転）が同じ判定を呼ぶことで、
    /// Update 実行順に依存せず両者が一致する（UiSceneVoid の ShouldVoid 共有と同型）。
    /// </summary>
    internal static class Talk2DBackdropGate
    {
        /// <summary>
        /// backdrop 適用条件が成立するか。成立時は t2d と active な m_bg を out で返す。
        /// 条件: Talk2DBackdrop 有効 + VR ready + UiSceneVoid 非適用画面 + active env が Talk2DScene
        ///       + m_bg が active（Home デートは m_bg inactive で除外）。
        /// </summary>
        public static bool IsActive(out Talk2DScene t2d, out GameObject bgGo)
        {
            t2d = null;
            bgGo = null;
            if (!Configs.EnableTalk2DBackdrop.Value || !VRModCore.IsVrActive) return false;
            if (GBSystem.Instance == null) return false;

            EnvSceneBase env = GBSystem.Instance.GetActiveEnvScene();
            // UiSceneVoid 適用画面では backdrop を退かせ void に完全非表示を任せる（void が勝つ）。
            bool voidWanted = Configs.EnableUiSceneVoid.Value
                && UiSceneVoid.UiSceneVoidPolicy.ShouldVoid(
                    GBSystem.GetCurrentSceneName(),
                    UiSceneVoid.EnvKindClassifier.Classify(env),
                    UiSceneVoid.MiniGameProbe.Stages3D());
            if (voidWanted) return false;

            t2d = env as Talk2DScene;
            if (t2d == null) return false;
            bgGo = t2d.m_bg;
            return bgGo != null && bgGo.activeInHierarchy;
        }
    }
}
