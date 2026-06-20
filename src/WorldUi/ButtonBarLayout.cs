using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// ボタン帯の配置（panel-local・z=0 平面）と表示判定用拡張矩形の純関数。
    /// ボタン辺は視角一定 + 加算（視点距離 × 比率 + オフセット）＝手前に引いても視角が肥大しない（spec §2）。
    /// 比率・加算は Configs.WorldUiButtonSizeRatio / WorldUiButtonSizeOffset
    ///（呼出元が解決して渡す＝テストは BepInEx 非依存の規約）。
    /// 間隔・マージン等の比率は固定値。index は 0=移動, 1=拡大, 2=曲面。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-auto-orient-design.md §2
    /// </summary>
    public static class ButtonBarLayout
    {
        public const float GapRatio = 0.025f;       // ボタン間隔 / パネル幅
        public const float MarginRatio = 0.03f;     // パネル下端→帯上端 / パネル幅
        public const float PadRatio = 0.02f;        // 拡張矩形の余白 / パネル幅
        public const int ButtonCount = 3;

        /// <summary>視点→パネル距離(m)× 視角比率 + 加算(m) からボタン辺(m)。
        /// 負の加算で 0 未満になる組合せのみ floor（負スケール quad の反転防止）。</summary>
        public static float ButtonSide(float eyeDistance, float sizeRatio, float sizeOffset)
            => Mathf.Max(0f, eyeDistance * sizeRatio + sizeOffset);

        /// <summary>index 番目のボタン中心（panel-local）。side は ButtonSide の結果を渡す。</summary>
        public static Vector3 ButtonCenter(int index, float panelWidth, float panelHeight, float side)
        {
            float g = panelWidth * GapRatio;
            float y = -panelHeight * 0.5f - panelWidth * MarginRatio - side * 0.5f;
            float x = (index - 1) * (side + g);
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// 表示判定用の拡張矩形（パネル本体 + 帯 + 余白を包む z=0 平面の矩形）。
        /// パネル→帯へレーザーを下ろす途中で帯が消えるフリッカを構造的に防ぐ。
        /// </summary>
        public static void ExpandedRect(float panelWidth, float panelHeight, float side,
            out Vector3 center, out float halfWidth, out float halfHeight)
        {
            float pad = panelWidth * PadRatio;
            float bottom = -panelHeight * 0.5f - panelWidth * MarginRatio - side; // 帯下端
            float top = panelHeight * 0.5f;
            center = new Vector3(0f, (top + bottom) * 0.5f, 0f);
            halfWidth = panelWidth * 0.5f + pad;
            halfHeight = (top - bottom) * 0.5f + pad;
        }
    }
}
