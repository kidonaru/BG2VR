using GB;
using GB.Save;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.DesktopLowRes
{
    /// <summary>
    /// VR 描画中、ゲームが windowed なら borderless フルスクリーンへ切り替え、VR 終了で元の
    /// DisplaySize へ復元する常駐 runner（VrDesktopLowRes トグルに束ねる）。フルスクリーン化後の
    /// 解像度低下は VrDesktopResolutionPatch（CalcFullScreenResolution Postfix）が担う。
    ///
    /// 復元値は VR on 時に退避した _savedSize を使う（VR off フレームに GBSystem.Update が先に
    /// SetDisplaySize(FULL_SCREEN) を撃って GetDisplaySize() が汚れる race を回避するため）。
    /// m_displaySize は VR 中のみメモリ上 FULL_SCREEN になるが、config 系 SaveData は通常プレイ中
    /// ディスク保存されないため実害はほぼ無い。VR off で必ず復元する。
    ///
    /// edge 判定は純関数 VrFullscreenPolicy.Decide に委譲（xUnit 済）。本 runner は SaveData I/O のみ。
    /// </summary>
    public sealed class VrDesktopFullscreenRunner : MonoBehaviour
    {
        private bool _forced;
        // _savedSize は _forced==true の間のみ有効（ForceFullscreen で _forced と必ずペアで代入される）。
        private DisplaySize _savedSize;

        private void Update()
        {
            bool want = VRModCore.IsVrActive && Configs.VrDesktopLowRes.Value;

            SaveData sd = GBSystem.Instance?.RefSaveData();
            if (sd == null) return; // ゲーム未初期化（SaveData 未生成）。

            bool currentWindowed = sd.GetDisplaySize() != DisplaySize.FULL_SCREEN;
            switch (VrFullscreenPolicy.Decide(want, _forced, currentWindowed))
            {
                case VrFullscreenAction.ForceFullscreen:
                    _savedSize = sd.GetDisplaySize();          // 退避は VR on 時のみ（汚れ前の値）。
                    _forced = true;
                    sd.SetDisplaySize(DisplaySize.FULL_SCREEN);
                    Plugin.Log?.LogInfo($"[VrDesktopFullscreen] VR 中フルスクリーン化（元: {_savedSize}）。");
                    break;

                case VrFullscreenAction.Restore:
                    // 退避値で復元する（VR off フレームに GBSystem.Update が SetDisplaySize(FULL_SCREEN) を
                    // 撃って GetDisplaySize() が汚れるため、その場では読まない）。逆に GBSystem.Update が本
                    // Restore より後に走ると Screen.fullScreen の遅延適用で最大 1 フレーム FULL_SCREEN が
                    // 残り得るが、_forced を false にするので再 Force されず次フレームで収束する（許容）。
                    _forced = false;
                    sd.SetDisplaySize(_savedSize);
                    Plugin.Log?.LogInfo($"[VrDesktopFullscreen] DisplaySize を復元（{_savedSize}）。");
                    break;
            }
        }
    }
}
