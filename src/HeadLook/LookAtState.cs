namespace BG2VR.HeadLook
{
    /// <summary>
    /// per-char の look-at 純状態（spec §5.1）。Unity 型に依存しない（xUnit 対象）。
    /// 角度はすべて「計算角」（ターゲットへのフル角度）空間で平滑し、出力時に適用率を掛ける。
    /// </summary>
    public sealed class LookAtState
    {
        /// <summary>角度ヒステリシス: 追従中か（engage で true / release で false）。</summary>
        public bool Engaged;

        /// <summary>首デッドゾーン: 移動中か（ズレ Start 超で true / Stop 以下で false）。</summary>
        public bool Moving;

        /// <summary>平滑中の首角（度・計算角ベース）。</summary>
        public float HeadYaw;
        public float HeadPitch;

        /// <summary>平滑中の目角（度・計算角ベース）。</summary>
        public float EyeYaw;
        public float EyePitch;
    }
}
