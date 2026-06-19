namespace BG2VR.VrInput
{
    /// <summary>手元モデルの per-hand コンテキスト（通常 / 各ミニゲーム）。HandModelSelector が
    /// コンテキスト別のループ順を持ち、ProjectorRunner が今のミニゲームから per-hand に解決して渡す。
    /// Cheki / Ahhn は片手のみ当該コンテキスト（反対手は Normal）。</summary>
    public enum HandModelContext
    {
        Normal = 0,
        Cheki = 1,
        Karaoke = 2,
        HandSumo = 3,
        Ahhn = 4,
        Drinking = 5, // NPC が飲み物を持つ間・設定手のみ（Hand 既定→Controller へ cycle 可）
    }
}
