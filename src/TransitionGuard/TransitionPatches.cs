using HarmonyLib;
using GB;
using GB.Game;

namespace BG2VR.TransitionGuard
{
    /// <summary>
    /// 遷移ガードのフック群（plan §3② / spec §4 / plan 2026-06-14）。
    ///
    /// teardown のタイミングを config で 2 段切替する:
    ///   - <c>TransitionRenderUntilUnload = false</c>（従来）: ChangeScene* の同期 Prefix
    ///     （最初の await より前）で即 teardown。フェードアウトは凍結フレーム上で走る。
    ///   - <c>TransitionRenderUntilUnload = true</c>（既定）: ChangeScene* 入場では arm のみ。
    ///     teardown は「遷移内の最初の <c>GBSystem.LoadSceneAsync</c>」まで遅延する。
    ///     フェードアウト await の全区間ぶん rig が live のまま＝フェードアウトが実シーン上で描画される。
    ///     teardown 実行点（LoadSceneAsync）は既に全黒到達後＝hold 開始は不可視。ロード/アンロードは
    ///     teardown 後＝「UnloadUnusedAssets 中に eye カメラが描画」（過去フリーズの根本原因）は踏まない。
    ///
    /// 完了検出は Postfix ではなく <see cref="TransitionGuardRunner"/> のポーリング（IsInputDisabled）。
    /// 多重発火は純状態機械が 1 回の teardown に集約する。
    ///
    /// arm/consume の scope（<see cref="TransitionTeardownArming"/>）が要る理由:
    /// 同じ <c>GBSystem.LoadSceneAsync</c> を env 切替（showEnvScene）や SetupEnvScenes も呼ぶ。
    /// これらは teardown 対象外（カメラ swap は fork のメインカメラ変更 → rig 自動再構築が吸収し
    /// VR セッション・描画は継続）。armed 済みのときだけ consume するので env 切替の load は無視される。
    ///
    /// ToAfter（ChangeSceneAsyncToAfter）だけは遅延しない: 実フローが BlackOut → UnloadUnusedAssets →
    /// LoadSceneAsync の順で UnloadUnusedAssets が load より先＝「フェード後・unload 前」に置ける後ろ倒し点が
    /// 存在しない（これが主因）。仮に UnloadUnusedAssets 直前で teardown したくても Resources.UnloadUnusedAssets
    /// は [NativeMethod] extern で Harmony フック不可（補強）。よって入場で即 teardown を維持する。
    /// </summary>
    // クラスレベル [HarmonyPatch] は必須。これが無いと Harmony.PatchAll() がこのクラスを発見せず、
    // メソッド側に [HarmonyPatch(...)] を付けても patch が一切適用されない（HarmonyX 仕様）。
    [HarmonyPatch]
    internal static class TransitionPatches
    {
        // ChangeScene* 入場で arm → 遷移内の最初の LoadSceneAsync で consume。
        private static readonly TransitionTeardownArming _arming = new TransitionTeardownArming();

        // ── arm 用 Prefix（config ON）/ 即時 teardown（config OFF）─────────

        // シーン再ロード遷移（日送り GameData.ToNextDay もここに合流）
        [HarmonyPatch(typeof(GBSystem), nameof(GBSystem.ChangeSceneReloadable))]
        [HarmonyPrefix]
        private static void ChangeSceneReloadable_Prefix()
            => ArmOrTeardown("GBSystem.ChangeSceneReloadable");

        [HarmonyPatch(typeof(GBSystem), nameof(GBSystem.ChangeSceneAsyncFromTo))]
        [HarmonyPrefix]
        private static void ChangeSceneAsyncFromTo_Prefix()
            => ArmOrTeardown("GBSystem.ChangeSceneAsyncFromTo");

        // ToAfter は UnloadUnusedAssets が load より先＝後ろ倒し不可 → 常に即時 teardown（安全維持）。
        [HarmonyPatch(typeof(GBSystem), nameof(GBSystem.ChangeSceneAsyncToAfter))]
        [HarmonyPrefix]
        private static void ChangeSceneAsyncToAfter_Prefix()
            => TransitionGuardRunner.NotifyTransitionStart("GBSystem.ChangeSceneAsyncToAfter");

        // ── consume 用 Prefix ────────────────────────────────

        // 遷移内の最初のシーンロードで armed を consume → teardown。
        // env 切替（showEnvScene）/ SetupEnvScenes の LoadSceneAsync は armed=false ＝無視。
        [HarmonyPatch(typeof(GBSystem), nameof(GBSystem.LoadSceneAsync))]
        [HarmonyPrefix]
        private static void LoadSceneAsync_Prefix()
        {
            if (_arming.TryConsume())
                TransitionGuardRunner.NotifyTransitionStart("GBSystem.LoadSceneAsync(遷移内)");
        }

        // config ON=arm（後ろ倒し）/ OFF=入場で即 teardown（従来動作）。
        // Prefix は毎回 .Value を読むので live 反映が自動成立（Subscribe 不要）。
        private static void ArmOrTeardown(string reason)
        {
            if (global::BG2VR.Configs.TransitionRenderUntilUnload.Value)
                _arming.Arm();
            else
                TransitionGuardRunner.NotifyTransitionStart(reason);
        }
    }
}
