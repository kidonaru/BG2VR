using HarmonyLib;
using UnityEngine;
using GB;

namespace BG2VR.VrInput
{
    /// <summary>Cheki 撮影中の視点固定: CameraControll() を 0 に注入する。ChekiCameraRunner が毎フレ FreezeAim をセット、
    /// ProjectorRunner 先頭で ResetFrame して失効を保証する（GbGameButtonBridge.Clear と同方式）。
    /// CameraControll=0 → ゲームの m_rotx/m_roty 増分0 → m_camera 静止 → 位置追従で rig も静止＝視点固定。
    /// 照準は ChekiCameraRunner の専用カメラが担うため m_camera の照準は不要。</summary>
    internal static class ChekiInputBridge
    {
        // メインスレッドからのみ読み書き（VR/ゲーム入力経路は single-thread）。
        public static bool FreezeAim;

        public static void ResetFrame() { FreezeAim = false; }
    }

    /// <summary>撮影中は CameraControll を 0 に（m_camera/rig を静止＝視点固定）。method Postfix。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.CameraControll))]
    internal static class GBInput_CameraControll_ChekiPatch
    {
        private static void Postfix(ref Vector2 __result)
        {
            if (ChekiInputBridge.FreezeAim) __result = Vector2.zero;
        }
    }
}
