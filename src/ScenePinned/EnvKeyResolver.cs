namespace BG2VR.ScenePinned
{
    /// <summary>
    /// アクティブ env scene 型名（＋進行中ミニゲーム種別）→ 固定 pose の保存キー（純関数・spec §3.1）。
    /// 非ミニゲーム時は env scene 型名をそのままキーにする（SteelFrameScene と env 不在は null＝保存対象外）。
    /// ミニゲーム進行中は env scene 型名 + "." + MiniGameType の複合キー（同一シーン上の複数ミニゲームを分離）。
    /// 鉄骨はミニゲーム枠で pinnable になる（SteelFrameScene 単独除外は非ミニゲーム時のみ維持）。
    /// デート系は全 BGType が型 Talk2DScene のため非ミニゲーム時は "Talk2DScene" に統合される。
    /// </summary>
    public static class EnvKeyResolver
    {
        public static string ResolveKey(string envSceneTypeName, string miniGameName)
        {
            // env 不在＝保存対象外（シーン文脈なしのワールド絶対キーは作らない・ミニゲーム有無を問わない）。
            if (string.IsNullOrEmpty(envSceneTypeName)) return null;

            // ミニゲーム進行中: env + 種別の複合キー。NONE は MiniGameType の番兵（非ミニゲーム）。
            // NUM は NONE と同値(=9)＝enum.ToString() は "NONE" を返すため実機で "NUM" は到達不可だが、
            // 将来の enum 変更に対する保険として明示的に弾く（純関数テストで両名を検証）。
            if (!string.IsNullOrEmpty(miniGameName) && miniGameName != "NONE" && miniGameName != "NUM")
                return envSceneTypeName + "." + miniGameName;

            // 非ミニゲーム: 従来どおり env scene 型名キー。鉄骨 env 単独は除外（実際には発生しないが従来挙動を保つ）。
            if (envSceneTypeName == "SteelFrameScene") return null;
            return envSceneTypeName;
        }
    }
}
