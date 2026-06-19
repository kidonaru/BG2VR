using UnityEngine;

namespace BG2VR.VrFade
{
    /// <summary>
    /// sprite の textureRect → VR overlay の texture bounds 値（正規化 UV）への変換（純ロジック）。
    /// D3D11 はテクスチャ原点が Unity と上下逆のため flipV=true で V を 1-v に写す
    /// （慣例: 全面 flip は {vMin=1, vMax=0}。実機検証 spec §4-3 で向きを確定させる）。
    /// </summary>
    public static class OverlayUvMapper
    {
        public struct Uv
        {
            public float UMin;
            public float VMin;
            public float UMax;
            public float VMax;
        }

        public static Uv Map(Rect textureRect, int texWidth, int texHeight, bool flipV)
        {
            // texture 未確定（サイズ 0）は全面 UV にフォールバック
            if (texWidth <= 0 || texHeight <= 0)
            {
                return flipV
                    ? new Uv { UMin = 0f, VMin = 1f, UMax = 1f, VMax = 0f }
                    : new Uv { UMin = 0f, VMin = 0f, UMax = 1f, VMax = 1f };
            }
            float u0 = textureRect.xMin / texWidth;
            float u1 = textureRect.xMax / texWidth;
            float v0 = textureRect.yMin / texHeight;
            float v1 = textureRect.yMax / texHeight;
            return flipV
                ? new Uv { UMin = u0, VMin = 1f - v0, UMax = u1, VMax = 1f - v1 }
                : new Uv { UMin = u0, VMin = v0, UMax = u1, VMax = v1 };
        }
    }
}
