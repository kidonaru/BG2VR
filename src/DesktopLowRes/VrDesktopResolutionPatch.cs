using System;
using GB;
using HarmonyLib;
using UnityVRMod.Core;

namespace BG2VR.DesktopLowRes
{
    /// <summary>
    /// VR 描画中、GB.GBSystem.CalcFullScreenResolution の返り値を低解像度（16:9・borderless）に
    /// 差し替えて、モニタ側フルスクリーン描画の GPU 負荷を下げる。
    ///
    /// GBSystem.Update() は fullscreen 時に毎フレーム CalcFullScreenResolution() を呼び、返り値と
    /// Screen が不一致なら RefSaveData().SetDisplaySize(FULL_SCREEN) で再アサートする
    /// (Assembly-CSharp/GB/GBSystem.cs:578-590)。VR 中だけ低解像度を返せば、ゲーム自身の再アサート機構が
    /// 低解像度を維持し、VR 終了で通常値へ自動復元する（生 Screen.SetResolution で毎フレーム戦わない）。
    /// SetDisplaySize → Screen.SetResolution(w,h,true) は Unity 2022+ で既定 FullScreenWindow（borderless）
    /// ＝モニタモードを変えずに backbuffer を縮小する。
    ///
    /// FixMod も同メソッドに Prefix を持つ（CalcFullScreenResolutionPatch, return false で original skip）。
    /// 本パッチは Postfix なので prefix の false 返却に関係なく必ず最後に走り、__result を上書きする
    /// （HarmonyPriority 調整も HarmonyX の prefix-skip 意味論への依存も不要＝堅牢）。
    ///
    /// adjustByWidth=true を返す＝GBSystem.Update は幅一致のみ照合し、高さは VrDesktopResolution.Derive の
    /// 4px snap で 16:9±0.05 内に収まる（アスペクトチェックも通過）＝再アサート flap しない。
    /// HMD eye 描画は VR runtime RT 独立＝Screen 解像度に非依存。
    ///
    /// 注: 非16:9 モニタ + FixMod FullscreenUltrawideEnabled 併用時は flap 可能性あり（spec §5 参照・実機検証で
    /// 観測したら本 Postfix に「モニタが概ね 16:9 のときのみ作動」gate を足す＝観測まで予防コードは入れない）。
    /// </summary>
    [HarmonyPatch(typeof(GBSystem), "CalcFullScreenResolution")]
    public static class VrDesktopResolutionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref ValueTuple<int, int, bool> __result)
        {
            if (!VRModCore.IsVrActive || !Configs.VrDesktopLowRes.Value)
                return; // 非 VR / 無効時は FixMod / vanilla の値をそのまま使う

            var (w, h) = VrDesktopResolution.Derive(Configs.VrDesktopWidth.Value);
            __result = new ValueTuple<int, int, bool>(w, h, true);
        }
    }
}
