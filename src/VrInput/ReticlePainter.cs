using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// レティクル（円形ソフトリング + 中心ドット）のピクセル生成（純関数・IconPainter と同パターン）。
    /// 白 × アルファのみ＝色は material.color で着色する。row0=下（Texture2D.SetPixels32 規約）だが
    /// 回転対称なので向きは不問。
    /// spec: docs/superpowers/specs/2026-06-13-bg2-vr-laser-visual-and-click-alignment-design.md §4.3
    /// </summary>
    public static class ReticlePainter
    {
        public const int Size = 64;
        // 正規化半径（テクスチャ半幅 = 1）
        private const float RingRadius = 0.78f;    // リング中心線
        private const float RingHalfWidth = 0.10f; // リング半幅（この範囲は α=1）
        private const float RingFeather = 0.08f;   // リング縁のぼかし幅
        private const float DotRadius = 0.10f;     // 中心ドット半径（この範囲は α=1）
        private const float DotFeather = 0.10f;    // ドット縁のぼかし幅

        public static Color32[] Paint()
        {
            var px = new Color32[Size * Size];
            float half = (Size - 1) * 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    // リング: 中心線からの距離の台形プロファイル / ドット: 半径内 1 → feather で 0
                    float ring = 1f - Mathf.Clamp01((Mathf.Abs(r - RingRadius) - RingHalfWidth) / RingFeather);
                    float dot = 1f - Mathf.Clamp01((r - DotRadius) / DotFeather);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Max(ring, dot) * 255f);
                    px[y * Size + x] = new Color32(255, 255, 255, a);
                }
            }
            return px;
        }
    }
}
