using HarmonyLib;
using UnityEngine;
using GB.Save;
using BG2VR.Patches;

namespace BG2VR.VrInput
{
    /// <summary>Saves.GetChekiTexture() の戻り値を、VF と同一の RT から作った写真へ差し替える（WYSIWYG）。
    /// 正方 RT 全面を Graphics.Blit で 320×320 へ縮小（中央クロップでなく全体）＝VF と構図一致。
    /// ChekiCameraRunner がアクティブ（撮影中・RT 有効）な時のみ。非アクティブ/例外は原実装（ScreenCapture）へ
    /// フォールスルー＝ミニゲームを壊さない。原 CaptureCheki の ScreenCapture は走るが戻り値を上書きするため無害。</summary>
    [HarmonyPatch(typeof(Saves), nameof(Saves.GetChekiTexture))]
    internal static class Saves_GetChekiTexture_ChekiPatch
    {
        private const int ChekiSize = 320; // ゲームの cheki テクスチャ一辺（Saves.cs 実測）

        // FixMod reflection 越境値（CapturedSize = ChekiSize）の入力検証域。FixMod 側 clamp と load 側検証
        //（ChekiItemLoadHiResPatch の 正方 + [64,2048]）に一致。FixMod が将来 clamp を変えても巨大 RT を作らない。
        private const int HiResSizeMin = 64;
        private const int HiResSizeMax = 2048;

        private static void Postfix(ref Texture2D __result)
        {
            RenderTexture rt = ChekiCameraRunner.ActiveRT;
            if (rt == null) return;       // 非 VR-cheki / latch 失効 → 原 ScreenCapture を使う
            if (__result == null) return; // 原実装が m_cheki を用意していない異常時は触らない
            RenderTexture tmp = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                tmp = RenderTexture.GetTemporary(ChekiSize, ChekiSize, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, tmp); // 正方 RT 全面 → 320² へ縮小（任意の RT 解像度に対応）
                RenderTexture.active = tmp;
                // 原実装が確保した m_cheki(__result) へ直接 ReadPixels で上書きする（新規 Texture2D を作らない）。
                // ChekiData.Set は tex.GetRawTextureData() のバイトコピーのみで Texture2D を保持/破棄しないため、
                // 新規生成すると誰も Destroy せず連続撮影でリークする。m_cheki はゲームが寿命管理（次回 CaptureCheki で破棄）。
                // m_cheki は 320×320 R8G8B8A8_SRGB（Saves.cs 実測）＝同寸法で ReadPixels がそのまま通り raw 長も一致。
                __result.ReadPixels(new Rect(0, 0, ChekiSize, ChekiSize), 0, 0, false);
                __result.Apply(false);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[ChekiCam] 写真キャプチャ失敗・原実装にフォールバック: {e.Message}");
                // ReadPixels 前の例外なら m_cheki 内容は原 ScreenCapture のまま＝無害
            }
            finally
            {
                RenderTexture.active = prev;
                if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
            }

            // FixMod 高解像度チェキ併用時: 保存される hi-res も VR カメラ構図に揃える（sidecar を VR 再レンダで上書き）。
            // FixMod 不在 / hi-res OFF / このショット未撮影なら no-op。例外は握り潰す（ゲームの GetChekiTexture を壊さない）。
            TryOverwriteHiRes();
        }

        /// <summary>FixMod hi-res sidecar を VR カメラ由来の hi-res で上書きする（併用時の WYSIWYG 整合）。</summary>
        private static void TryOverwriteHiRes()
        {
            try
            {
                if (ChekiCameraRunner.ActiveRT == null) return;       // 非 VR-cheki / latch 失効
                if (!FixModChekiHiResBridge.HasFreshHiRes()) return;  // FixMod 不在 / hi-res OFF / このショット未撮影
                int size = FixModChekiHiResBridge.TargetSize();
                if (size <= 0) return;
                size = Mathf.Clamp(size, HiResSizeMin, HiResSizeMax); // reflection 越境値の入力検証（巨大確保防止）
                Texture2D vrHi = ChekiCameraRunner.RenderHiResForActive(size);
                if (vrHi == null) return;                             // cam 不在 → FixMod スクショ据え置き（無害）
                FixModChekiHiResBridge.Store(vrHi, size);             // Store が旧 tex 破棄・所有権取得（二重破棄なし）
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[ChekiHiRes] hi-res 連携でエラー・FixMod スクショへフォールバック: {e.Message}");
            }
        }
    }
}
