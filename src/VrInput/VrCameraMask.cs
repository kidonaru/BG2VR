namespace BG2VR.VrInput
{
    /// <summary>カメラ cullingMask を構築する純関数（UnityEngine 非依存・テスト host で可）。
    /// instax ビューファインダカメラは ~0(Everything) から VR visuals 層(30=instax モデル/手/画面) を除外して使う
    /// （シーンカメラの cullingMask は EyeCullingCoordinator が毎フレ上書きするため基準に使わない）。</summary>
    public static class VrCameraMask
    {
        /// <summary>baseMask から指定レイヤのビットを落とす。</summary>
        public static int Exclude(int baseMask, params int[] layers)
        {
            int m = baseMask;
            foreach (int l in layers) m &= ~(1 << l);
            return m;
        }
    }
}
