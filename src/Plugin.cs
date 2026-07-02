using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityVRMod.Core;
using BG2VR.TransitionGuard;
using BG2VR.CameraBridge;
using BG2VR.DesktopLowRes;

namespace BG2VR
{
    /// <summary>
    /// BG2VR エントリポイント。VR コア（UnityVRMod-fork）に依存し、BG2 固有の
    /// 遷移ガード / カメラ供給を Harmony で注入する companion プラグイン。
    /// </summary>
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(VRModCore.GUID)] // UnityVRMod-fork を先にロードさせる（VR コア必須）
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            // vendored ロガー初期化（PatchLogger は独自 _logger を持つ。未呼出だとパネル系ログが無音）。
            BG2VR.Utils.PatchLogger.Initialize(Logger);

            // 生成 config を bind（PatchAll / runner / パネルが Value を参照するため最初に）。
            global::BG2VR.Configs.BindAll(Config);

            // セーフモードキーは BG2VR 側の HotkeyConfig で管理する。fork 側の重複処理を無効化。
            UnityVRMod.Core.VRModKeybind.ExternallyManaged = true;

            // パネルのグループ折りたたみ状態を bind（必須: 未呼出だと IsCollapsed/SetCollapsed で NRE）。
            BG2VR.Patches.Settings.SettingsCollapseState.Init(Config);

            // 常駐 runner（遷移ガード + カメラ供給）。シーンをまたいで生存させる。
            var go = new GameObject("BG2VR_Runtime");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<TransitionGuardRunner>();
            go.AddComponent<CameraBridgeRunner>();
            go.AddComponent<VrDesktopFullscreenRunner>(); // VR 中フルスクリーン化（VrDesktopLowRes に束ね）。
            go.AddComponent<BG2VR.WorldUi.ProjectorRunner>(); // Phase3 WorldUiProjector（ゲーム UI を world 投影）。
            go.AddComponent<BG2VR.VrFade.VrFadeRunner>(); // ゲーム ScreenFade を compositor fade へミラー（VR フェード）。
            go.AddComponent<BG2VR.VrFade.TransitionOverlayRunner>(); // 遷移絵柄を VR compositor overlay へミラー（rig teardown 中も表示）。
            go.AddComponent<BG2VR.Patches.FreeCameraVrGuardRunner>(); // FixMod FreeCamera の VR 時強制無効（rising edge 監視）。
            go.AddComponent<BG2VR.Talk2DBackdrop.Talk2DBackdropRunner>(); // 2D背景イベントの背景遠景化+周辺暗転（Talk2D Backdrop）。
            go.AddComponent<BG2VR.HeadLook.HeadLookRunner>(); // キャラの顔/視線を HMD に向ける（Head/Eye Look-At）。
            go.AddComponent<BG2VR.FramePacing.FramePacingRunner>(); // VR 中の FPS 上限撤廃（60fps キャップ起因のリプロジェクションちらつき解消）。
            go.AddComponent<BG2VR.EyeMsaa.EyeMsaaRunner>(); // VR 中 URP msaaSampleCount を VrEyeMsaa へ駆動（MSAA 本体）。
            go.AddComponent<BG2VR.EyeCulling.EyeCullingCoordinator>(); // eye の cullingMask/clear を毎フレ stateless 所有（void/dim/normal を排他解決）。
            go.AddComponent<BG2VR.PostProcess.PostProcessCoordinator>(); // ゲームの post-process(グレーディング+Bloom)を eye に反映（DoF/CA は抑制）。
            go.AddComponent<BG2VR.ScenePinned.ScenePinnedPoseRunner>(); // 保存済み env を固定位置へ配置（カメラ追従を抑止）。follow より先に登録（抑止フラグを先に確定）。
            go.AddComponent<BG2VR.CameraFollow.CameraPositionFollowRunner>(); // ゲームカメラの位置変化を rig へ差分転写（回転無追従）。
            go.AddComponent<BG2VR.Locomotion.RecenterRunner>(); // 起動時(初回)+両手Grip長押しで正面リセット（fork の reference space recenter を発火）。
            go.AddComponent<BG2VR.SpatialVoice.SpatialVoiceRunner>(); // VR 中、キャストのボイスを Steam Audio HRTF で空間化（本体 voice をミラー）。
            go.AddComponent<BG2VR.HandLighting.HandLightingRunner>(); // 手モデル専用 layer 28 を照らす自前 directional light（scene 光から isolation）。
            go.AddComponent<BG2VR.MouseSuppress.MouseSuppressionRunner>(); // VR 中の物理マウス無効化（UI/カメラがマウス座標に反応するのを止める）。
            go.AddComponent<BG2VR.LeakFix.NativeRenderPassDisableRunnerBehaviour>(); // URP native render pass を無効化して D3D12 NON_LOCAL leak（エンジンバグ）を止める。

            // 設定パネル（F10）。comfort スライダー（ConfigElement 直結）を注入してから初期化する。
            // 注: SetExtraEntries は SettingsController.Initialize より前に呼ぶ
            //     （SettingsView.Awake が BuildPanel→AllEntries() を同期的に読むため）。
            BG2VR.Patches.Settings.SettingsView.SetExtraEntries(BG2VR.Config.ComfortEntries.Build());
            BG2VR.Patches.Settings.SettingsController.Initialize(go);

            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            // FixMod FreeCamera ガード（reflection 解決＝PatchAll 対象外。FixMod 不在なら no-op）。
            BG2VR.Patches.FreeCameraVrGuard.TryInstall(harmony);

            // patch 適用件数を検証。0 件なら TransitionGuard が機能せず遷移フリーズが再発するため明示する。
            int patched = harmony.GetPatchedMethods().Count();
            if (patched > 0)
                Log.LogInfo($"Harmony patch 適用: {patched} メソッド。");
            else
                Log.LogError("Harmony patch が 0 件。TransitionGuard が機能しません（クラスレベル [HarmonyPatch] を確認）。");

            Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} {MyPluginInfo.PLUGIN_VERSION} 初期化完了。");
        }

        // 設定パネル操作中のゲーム入力抑制。VR/desktop では実害が薄いため現状 no-op。
        // SettingsController（ShouldSuppressHotkey 経由）と vendored HotkeyConfig が参照するため
        // API は用意する（実機で入力競合を観測したら実装する）。
        public static void SuppressGameInputTemporarily() { }
        public static bool ShouldSuppressGameInput() => false;
    }
}
