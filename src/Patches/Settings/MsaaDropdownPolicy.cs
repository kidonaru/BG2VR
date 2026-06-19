namespace BG2VR.Patches.Settings
{
    /// <summary>MSAA dropdown の index↔value マッピング（純関数・UI 専用）。
    /// 有効値 {1,2,4,8} は RenderTexture.antiAliasing の制約（fork EyeRtPolicy.SanitizeMsaa と同集合）。
    /// dropdown は ValueFromIndex で必ず有効値しか書かないが、.cfg 手編集の非有効値も
    /// IndexFromValue で下方向丸めして最寄り有効値の index に対応づける（表示と実描画の乖離を最小化）。</summary>
    internal static class MsaaDropdownPolicy
    {
        /// <summary>dropdown ラベル「オフ/2x/4x/8x」と同順の有効 MSAA 値。</summary>
        public static readonly int[] Values = { 1, 2, 4, 8 };

        /// <summary>dropdown 選択 index → MSAA 値（範囲外は端へクランプ）。</summary>
        public static int ValueFromIndex(int index)
        {
            if (index < 0) index = 0;
            if (index >= Values.Length) index = Values.Length - 1;
            return Values[index];
        }

        /// <summary>MSAA 値 → dropdown index。非有効値は下方向丸め（&lt;1 はオフ=0）。</summary>
        public static int IndexFromValue(int value)
        {
            int idx = 0;
            for (int i = 0; i < Values.Length; i++)
                if (value >= Values[i]) idx = i;   // 昇順なので「値以下の最大の有効値」の index に収束
            return idx;
        }

        /// <summary>任意の int を有効 MSAA 値 {1,2,4,8} へ正規化（下方向丸め）。URP msaaSampleCount 設定用。</summary>
        public static int Sanitize(int value) => ValueFromIndex(IndexFromValue(value));
    }
}
