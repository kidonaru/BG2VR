using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.Patches
{
    /// <summary>
    /// FixMod FreeCamera の VR 時強制無効ガード（spec §5）。FixMod へのコンパイル参照なし
    /// （AccessTools.TypeByName で解決。FixMod 不在なら何もしない）。
    /// Why: FreeCam は現行カメラを CopyFrom した別カメラ + ゲーム UI 抑制を行うため、VR 中に動くと
    /// fork のカメラ解決と WorldUiProjector の投影元 canvas が二重に破綻する。VR との併用ユースケースは無い。
    /// メンバー可視性: Activate=private / Deactivate=public / IsActive=public static（AccessTools はどちらも解決可）。
    /// </summary>
    internal static class FreeCameraVrGuard
    {
        private static Type s_managerType;
        private static PropertyInfo s_isActive;
        private static MethodInfo s_deactivate;

        public static void TryInstall(Harmony harmony)
        {
            s_managerType = AccessTools.TypeByName("BunnyGarden2FixMod.Patches.FreeCamera.FreeCameraManager");
            if (s_managerType == null)
            {
                Plugin.Log.LogInfo("[FreeCamGuard] FixMod FreeCameraManager 不在のためガード無効（FixMod 未導入なら正常）。");
                return;
            }
            MethodInfo activate = AccessTools.Method(s_managerType, "Activate");
            s_isActive = AccessTools.Property(s_managerType, "IsActive");
            s_deactivate = AccessTools.Method(s_managerType, "Deactivate");
            if (activate == null || s_isActive == null || s_deactivate == null)
            {
                Plugin.Log.LogWarning("[FreeCamGuard] FreeCameraManager のメンバー解決に失敗。ガード無効。");
                s_managerType = null;
                return;
            }
            harmony.Patch(activate, prefix: new HarmonyMethod(typeof(FreeCameraVrGuard), nameof(ActivatePrefix)));
            Plugin.Log.LogInfo("[FreeCamGuard] FreeCamera VR ガードを適用。");
        }

        private static bool ActivatePrefix()
        {
            if (!VRModCore.IsVrActive) return true;
            Plugin.Log.LogInfo("[FreeCamGuard] VR 中のため FreeCamera の起動をスキップ。");
            return false; // Activate を実行しない
        }

        /// <summary>VR rising edge で呼ぶ。FreeCam がアクティブなら強制解除（VR 起動前から ON だったケース）。</summary>
        public static void ForceDeactivateIfActive()
        {
            if (s_managerType == null) return;
            if (!(bool)s_isActive.GetValue(null)) return;
            // FixMod の plugin GO は hidden の可能性があるため FindObjectsOfTypeAll で探す。
            // 万一複数 instance がいても Deactivate は冪等（OnDisable から無条件で呼ばれる前提の
            // null 安全実装）なので全件に呼ぶ（[0] 決め打ちだと非 active 側を掴む可能性・plan-review 指摘）。
            var instances = Resources.FindObjectsOfTypeAll(s_managerType);
            if (instances == null || instances.Length == 0) return;
            foreach (var inst in instances) s_deactivate.Invoke(inst, null);
            Plugin.Log.LogInfo("[FreeCamGuard] VR 有効化に伴い FreeCamera を強制解除。");
        }
    }

    /// <summary>IsVrActive の rising edge を監視して FreeCam を強制解除する常駐 runner。</summary>
    internal sealed class FreeCameraVrGuardRunner : MonoBehaviour
    {
        private bool m_prev;

        private void Update()
        {
            bool active = VRModCore.IsVrActive;
            if (active && !m_prev) FreeCameraVrGuard.ForceDeactivateIfActive();
            m_prev = active;
        }
    }
}
