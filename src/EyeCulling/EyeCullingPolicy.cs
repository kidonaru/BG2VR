using UnityEngine;

namespace BG2VR.EyeCulling
{
    /// <summary>
    /// eye カメラの cullingMask / clear を決める純関数（UnityEngine の enum/Color のみ依存・
    /// BepInEx/ゲーム型 非依存＝xUnit テスト可能）。void > dim > normal の排他で 1 状態へ解決する。
    /// base = Everything(-1)（実測でゲーム主カメラ = 0xFFFFFFFF と同値）。
    /// </summary>
    public static class EyeCullingPolicy
    {
        /// <summary>解決済み eye 描画設定。</summary>
        public readonly struct EyeCullingState
        {
            public readonly int CullingMask;
            public readonly CameraClearFlags ClearFlags;
            public readonly Color BackgroundColor;

            public EyeCullingState(int cullingMask, CameraClearFlags clearFlags, Color backgroundColor)
            {
                CullingMask = cullingMask;
                ClearFlags = clearFlags;
                BackgroundColor = backgroundColor;
            }
        }

        /// <summary>全ゲーム world layer を描く base mask（実測でゲーム主カメラと同値の Everything）。</summary>
        public const int BaseMask = -1;

        /// <summary>
        /// eye 描画設定を解決する。voidActive が dimActive に優先する（排他）。
        /// voidBrightness / dimBrightness は SolidColor 暗転のグレー値（0-1）。
        /// </summary>
        public static EyeCullingState Resolve(bool voidActive, bool dimActive, float voidBrightness, float dimBrightness)
        {
            if (voidActive)
                // VR 視覚物 2 層（UI=30 / レーザー・コントローラ=29）。HandLighting(28) はここに含めない＝
                // fork の SetVrModelOverlay 経路で main pass 除外 + overlay pass で最前面描画される（UI 同様）。
                // 含めるとレーザー越しに見えなくなり、また cullingMask が main pass で立つと二重描画する。
                return new EyeCullingState(
                    VrLayers.VisualsMask | VrLayers.VisualsPostProcessedMask,
                    CameraClearFlags.SolidColor, Gray(voidBrightness));
            if (dimActive)
                return new EyeCullingState(BaseMask, CameraClearFlags.SolidColor, Gray(dimBrightness));
            return new EyeCullingState(BaseMask, CameraClearFlags.Skybox, Color.black);
        }

        private static Color Gray(float v) => new Color(v, v, v, 1f);
    }
}
