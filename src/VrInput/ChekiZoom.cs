using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>instax ビューファインダカメラのズーム（FOV）。stickY>0=ズームイン(FOV減)。
    /// Step は純関数（UnityEngine.Mathf のみ＝テスト host で可）。CurrentFov はランナーが毎フレ更新する共有値で、
    /// ChekiCameraRunner がカメラ FOV に適用する。</summary>
    public static class ChekiZoom
    {
        /// <summary>ChekiCameraRunner が更新し、カメラ FOV に使う現在値（撮影開始時に既定へリセット）。</summary>
        public static float CurrentFov = 50f;

        /// <summary>現在 FOV に stickY×speed×dt を反映（stickY>0 で FOV 減＝ズームイン）し min..max にクランプ。</summary>
        public static float Step(float currentFov, float stickY, float speed, float dt, float min, float max)
        {
            return Mathf.Clamp(currentFov - stickY * speed * dt, min, max);
        }
    }
}
