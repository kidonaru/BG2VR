namespace BG2VR.WorldUi
{
    /// <summary>
    /// 投影 canvas root の layer マップ規則（純関数・UnityEngine 非依存）。
    /// ScreenSpaceCamera 化した canvas は root の layer が UI カメラ cullingMask に
    /// 含まれないと丸ごと cull される。Default(0) は quad/3D 用に UI カメラから除外するため、
    /// Default に居る投影 canvas は UI(5) へ寄せて描画対象にする。
    /// </summary>
    public static class CanvasLayerPolicy
    {
        public const int DefaultLayer = 0;
        public const int UiLayer = 5;

        /// <summary>Default(0) は UI(5) へ寄せる。それ以外はそのまま。</summary>
        public static int EffectiveLayer(int layer)
        {
            return layer == DefaultLayer ? UiLayer : layer;
        }
    }
}
