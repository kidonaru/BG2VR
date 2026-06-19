using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// ボタンアイコンのピクセル生成（純関数・フォント依存ゼロ）。暗背景 + 白グリフの不透明
    /// テクスチャ（透過なし＝Unlit opaque でブレンド設定不要）。row0=下（Texture2D.SetPixels32 規約）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-adjust-buttons-design.md §5
    /// </summary>
    public static class IconPainter
    {
        public const int Size = 64;
        private static readonly Color32 Bg = new Color32(28, 30, 40, 255);
        private static readonly Color32 Fg = new Color32(235, 240, 250, 255);

        public static Color32[] Paint(PanelButtonKind kind)
        {
            var px = new Color32[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = Bg;
            switch (kind)
            {
                case PanelButtonKind.Move: PaintMove(px); break;
                case PanelButtonKind.Scale: PaintScale(px); break;
                case PanelButtonKind.Curve: PaintCurve(px); break;
            }
            return px;
        }

        // 十字 + 4 方向の矢頭
        private static void PaintMove(Color32[] px)
        {
            FillRect(px, 14, 30, 49, 33); // 横バー
            FillRect(px, 30, 14, 33, 49); // 縦バー
            for (int i = 0; i < 7; i++)
            {
                int half = 7 - i;
                FillRect(px, 13 - i, 31 - half, 13 - i, 32 + half); // 左矢頭
                FillRect(px, 50 + i, 31 - half, 50 + i, 32 + half); // 右矢頭
                FillRect(px, 31 - half, 13 - i, 32 + half, 13 - i); // 下矢頭
                FillRect(px, 31 - half, 50 + i, 32 + half, 50 + i); // 上矢頭
            }
        }

        // 斜め双方向矢印（左下 ⇄ 右上）
        private static void PaintScale(Color32[] px)
        {
            for (int i = 0; i < 28; i++) FillRect(px, 17 + i, 17 + i, 19 + i, 19 + i); // 斜めバー
            FillRect(px, 14, 14, 27, 17); FillRect(px, 14, 14, 17, 27);                // 左下 L 字矢頭
            FillRect(px, 36, 46, 49, 49); FillRect(px, 46, 36, 49, 49);                // 右上 L 字矢頭
        }

        // 上向きに張る弧（曲面の象徴）
        private static void PaintCurve(Color32[] px)
        {
            for (int a = -60; a <= 60; a += 2)
            {
                float rad = a * Mathf.Deg2Rad;
                int x = 32 + (int)(22f * Mathf.Sin(rad));
                int y = 14 + (int)(26f * Mathf.Cos(rad));
                FillRect(px, x - 1, y - 1, x + 1, y + 1);
            }
        }

        private static void FillRect(Color32[] px, int x0, int y0, int x1, int y1)
        {
            int xa = Mathf.Max(0, Mathf.Min(x0, x1)), xb = Mathf.Min(Size - 1, Mathf.Max(x0, x1));
            int ya = Mathf.Max(0, Mathf.Min(y0, y1)), yb = Mathf.Min(Size - 1, Mathf.Max(y0, y1));
            for (int y = ya; y <= yb; y++)
                for (int x = xa; x <= xb; x++)
                    px[y * Size + x] = Fg;
        }
    }
}
