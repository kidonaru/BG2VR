using System;

namespace BG2VR.DesktopLowRes
{
    /// <summary>
    /// VR 中に固定するデスクトップ解像度を、設定の幅から 16:9 で算出する純関数。
    /// 高さは width×9/16 を 4px 単位に丸める（奇数/半端な高さを避ける）。
    /// snap の移動量は最大 2px で、GBSystem.Update のアスペクト許容 ±0.05 に収まるため
    /// 再アサート flap を起こさない。UnityEngine 非依存（System.Math のみ）＝xUnit 対象。
    /// </summary>
    internal static class VrDesktopResolution
    {
        /// <summary>幅(px) から (幅, 16:9 で 4px snap した高さ) を算出する。</summary>
        public static (int width, int height) Derive(int width)
        {
            // 退行入力に対する防御（slider は 480..1920 を強制するが純関数として安全側に）。
            int w = Math.Max(16, width);
            // 幅を偶数に snap。手編集の奇数幅でドライバが幅を偶数丸めすると Screen.width が
            // adjustByWidth=true の幅照合（== w）と恒久的にズレて毎フレーム再アサート flap するため、
            // 出力段で偶数を保証して構造的に排除する（高さの 4px snap と同じ出力正規化）。
            w -= w & 1;
            // 16:9 高さを算出し、4px 単位に snap（半端/奇数高さを避ける）。
            int rawH = (int)Math.Round(w * 9.0 / 16.0, MidpointRounding.AwayFromZero);
            int h = (int)Math.Round(rawH / 4.0, MidpointRounding.AwayFromZero) * 4;
            if (h < 4) h = 4;
            return (w, h);
        }
    }
}
