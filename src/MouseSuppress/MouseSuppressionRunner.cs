using UnityEngine;
using UnityEngine.InputSystem;
using UnityVRMod.Core;

namespace BG2VR.MouseSuppress
{
    /// <summary>
    /// VR 描画中（IsVrActive）に物理マウスデバイスを無効化する常駐 runner。
    /// ゲーム UI の InputSystemUIInputModule・GBInput のマウス読み（CameraControll/LeftClick/UpdateMouse）が
    /// 実マウス座標に反応するのを止める。mod の VR 入力は全て Harmony Postfix / 直 ExecuteEvents 注入で
    /// マウスデバイス非依存のため、無効化しても VR 操作は壊れない。
    /// FramePacingRunner と同型の edge runner（rising=Disable / falling=Enable）＋定常再適用（Reassert）で、
    /// VR 中にマウスが接続/再接続/差し替わっても取りこぼさず無効化する（要件「全面無効」）。
    /// gate は IsVrActive かつ !IsUserSafeModeActive。F11(RigReinitOnToggle) は rig だけ teardown して
    /// session を維持＝IsVrActive(=IsVrReady) が true のまま残るため、!IsUserSafeModeActive を併せて見ないと
    /// VR 無効化後もマウスが復帰しない（実機報告で判明）。
    /// IsVrActive は standby/遷移で false になる経路があり、その間もマウスが自動復帰する
    /// （HMD doff 中にデスクトップ操作可能）。falling/rising の edge ごとに enable/disable が走るが、
    /// それらの窓は IsInputDisabled 済みでマウス座標反応の実害なし。
    /// 注: DisableDevice は GBInput.s_inputDevice を書き換えないため、固着時に IsKeyboardUsing() が VR 中
    /// true を返し FittingRoom 等のコントローラ分岐に影響しうる（実機検証で切り分け・投機的対処は入れない）。
    /// </summary>
    internal sealed class MouseSuppressionRunner : MonoBehaviour
    {
        private bool m_prevEffective;
        private bool m_disabled;            // 自分が無効化したか（他要因の無効化は復元しない）
        private InputDevice m_disabledDevice;

        private void Update()
        {
            // 毎フレ read ＝ F10 トグルで即 live 反映（ON↔OFF で disable/enable edge が走る）。
            // !IsUserSafeModeActive: F11 で VR 無効化中はマウスを復帰させる（RigReinit は IsVrActive が残るため必須）。
            bool effective = VRModCore.IsVrActive
                && !VRModCore.IsUserSafeModeActive
                && global::BG2VR.Configs.SuppressMouseInVr.Value;

            // 定常中に、まだ無効化していない有効な Mouse.current が存在するか（VR 中の接続/再接続/差し替わり検知）。
            Mouse mouse = Mouse.current;
            bool needsReassert = mouse != null && mouse.enabled
                && !(m_disabled && ReferenceEquals(m_disabledDevice, mouse));

            switch (MouseSuppressionPolicy.Decide(m_prevEffective, effective, needsReassert))
            {
                // rising edge と定常再適用は同じ処理（現在の Mouse.current を無効化して捕捉を最新へ更新）。
                case MouseSuppressionAction.Disable:
                case MouseSuppressionAction.Reassert:
                    // 既に無効なら触らない（他要因の無効化を尊重し、復元責任を持たない）。
                    if (mouse != null && mouse.enabled)
                    {
                        m_disabledDevice = mouse;
                        InputSystem.DisableDevice(m_disabledDevice);
                        m_disabled = true;
                        Plugin.Log.LogInfo("[MouseSuppress] VR 中の物理マウスを無効化。");
                    }
                    break;

                case MouseSuppressionAction.Enable:
                    if (m_disabled)
                    {
                        if (m_disabledDevice != null)
                            InputSystem.EnableDevice(m_disabledDevice);
                        m_disabled = false;
                        m_disabledDevice = null;
                        Plugin.Log.LogInfo("[MouseSuppress] VR 終了 → 物理マウスを復元。");
                    }
                    break;
            }

            m_prevEffective = effective;
        }

        // 兄弟 runner（FramePacing 等）と対称の復元保証。VR active のまま破棄される経路は現状ないが
        // （BG2VR_Runtime は常駐）、将来の動的 teardown でも無効化を残置しない。
        private void OnDestroy()
        {
            if (m_disabled && m_disabledDevice != null)
                InputSystem.EnableDevice(m_disabledDevice);
            m_disabled = false;
            m_disabledDevice = null;
        }
    }
}
