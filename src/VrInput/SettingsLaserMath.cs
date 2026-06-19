using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// 設定パネル(UI Toolkit)のレーザー操作で使う座標変換の純関数（UnityEngine の Vector2/Rect のみ依存）。
    /// quad raycast の UV（RT 全面・原点左下）→ panel 座標（visualTree 空間・Y 上原点）と、
    /// スライダートラック上の panelX → 正規化値(0..1) を担う。Configs は読まず解決済み値を引数で受ける。
    /// </summary>
    public static class SettingsLaserMath
    {
        /// <param name="uv">QuadRaycaster.Hit.Pixel / RtSize（0..1・原点左下）。</param>
        /// <param name="full">panel.visualTree.worldBound（panel 空間 full 矩形・原点左上）。</param>
        public static Vector2 UvToPanelPoint(Vector2 uv, Rect full)
        {
            // RT 左下原点 → panel 左上原点へ Y 反転して full 矩形へ写像する。
            return new Vector2(uv.x * full.width + full.x, (1f - uv.y) * full.height + full.y);
        }

        /// <summary>panelX をトラック [trackXMin, trackXMin+trackWidth] に対する 0..1 へ（クランプ込み）。</summary>
        public static float PanelXToNormalized(float panelX, float trackXMin, float trackWidth)
        {
            if (trackWidth <= 0f) return 0f;
            return Clamp01((panelX - trackXMin) / trackWidth);
        }

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
