using GB;
using UnityEngine;
using UnityVRMod.Core;
using BG2VR.UiSceneVoid;
using BG2VR.Talk2DBackdrop;

namespace BG2VR.EyeCulling
{
    /// <summary>
    /// eye カメラの cullingMask / clearFlags / backgroundColor を毎フレ stateless 再計算で決め、
    /// fork へ override として push する policy 所有者（spec 2026-06-13）。void（UI-only 画面で裏 3D
    /// 非表示）/ dim（Talk2D 暗転）/ normal を排他で解決し、解決済み 3 状態を
    /// <see cref="VRModCore.SetEyeCullingOverride"/> で渡す。実適用は fork の RenderEye が描画直前に行う
    /// （単一所有点）。eye カメラを自前で列挙・直書きしない＝eye の enabled フラグや MonoBehaviour 間の
    /// 実行順に依存しない（OpenVR→OpenXR backend 切替で eye が enabled=true + game mask 上書きされ
    /// coordinator がラッチアウトされた回帰の構造的解消。spec §3）。
    ///
    /// gating: IsVrActive(=IsVrReady) が false の間（VR OFF / 遷移 teardown / standby）は override を
    /// 解除して退く。eye 描画自体が起きない（rig 不在）か fork の game-copy で足り、判定群の空回りも避ける。
    /// </summary>
    internal sealed class EyeCullingCoordinator : MonoBehaviour
    {
        private bool m_loggedVoid;
        private bool m_loggedDim;
        private string m_loggedScene;

        private void Update()
        {
            // VR rig が描画可能でない間（VR OFF / 遷移 teardown / standby）は override を解除して退く。
            // eye 描画自体が起きない（rig 不在）か fork の game-copy で足り、判定群の空回りも避ける。
            if (!VRModCore.IsVrActive)
            {
                VRModCore.SetEyeCullingOverride(false, 0, default, default);
                return;
            }

            // void 判定（純関数 ShouldVoid に非純粋入力を渡す）。
            EnvKind envKind = EnvKindClassifier.Classify(
                GBSystem.Instance != null ? GBSystem.Instance.GetActiveEnvScene() : null);
            bool voidActive = Configs.EnableUiSceneVoid.Value
                && UiSceneVoidPolicy.ShouldVoid(GBSystem.GetCurrentSceneName(), envKind, MiniGameProbe.Stages3D());

            // dim 判定（Talk2D と共有 Gate）。void が勝つので voidActive 時は dim を見ない。
            bool dimActive = !voidActive && Talk2DBackdropGate.IsActive(out _, out _);

            var state = EyeCullingPolicy.Resolve(
                voidActive, dimActive,
                Configs.UiSceneVoidBrightness.Value, Configs.Talk2DVoidBrightness.Value);

            // void/dim/normal の 3 状態すべてを active=true で push（fork が単一所有者として適用）。
            // normal も -1/Skybox を明示 push＝base 固定化を踏襲し fork の stale な _mainCamera* を使わせない。
            VRModCore.SetEyeCullingOverride(true, state.CullingMask, state.ClearFlags, state.BackgroundColor);

            // 状態変化時のみログ（void↔dim 遷移も拾う）。
            string scene = GBSystem.GetCurrentSceneName();
            if (voidActive != m_loggedVoid || dimActive != m_loggedDim || scene != m_loggedScene)
            {
                Plugin.Log.LogInfo($"[EyeCulling] void={voidActive} dim={dimActive} scene={scene}");
                m_loggedVoid = voidActive;
                m_loggedDim = dimActive;
                m_loggedScene = scene;
            }
        }
    }
}
