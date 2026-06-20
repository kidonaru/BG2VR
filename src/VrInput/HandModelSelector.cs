namespace BG2VR.VrInput
{
    /// <summary>
    /// per-hand の手元モデル種別を、現在のコンテキスト（通常 / 各ミニゲーム）に応じて解決する純ロジック
    /// （UnityEngine / UnityVRMod 非依存＝テスト host で可）。grip+トリガーのコンボ（ModelSwitchInput）が
    /// cycleRequested を立てて切替を駆動する。ProjectorRunner がフィールドとして保持し **Teardown では消さない**
    /// ＝セッション内で遷移をまたいで保持。
    ///
    /// 通常コンテキストの選択（normalIndex）はミニゲームをまたいでも保持（既定 両手 Controller＝レーザー即使用可・
    /// ゲーム再起動で既定へ戻る）。特殊コンテキストの選択（contextIndex）は **そのコンテキストへ突入した立上りで
    /// 既定（order 先頭＝プロップ/カメラ/ハンド）へリセット**する（突入毎にプロップから開始）。
    /// インデックス: isLeft=true で左手 / false で右手。
    /// </summary>
    public sealed class HandModelSelector
    {
        // 各コンテキストのループ順（先頭 = 突入時の既定＝従来の強制 override 種別）。
        // Camera / Tambourine / GlowStick は対応コンテキストでのみ出現し、Normal には出ない。
        private static readonly HandModelKind[] NormalOrder       = { HandModelKind.Controller, HandModelKind.Hand };
        private static readonly HandModelKind[] KaraokeLeftOrder  = { HandModelKind.Tambourine, HandModelKind.Controller, HandModelKind.Hand };
        private static readonly HandModelKind[] KaraokeRightOrder = { HandModelKind.GlowStick,  HandModelKind.Controller, HandModelKind.Hand };
        private static readonly HandModelKind[] ChekiOrder        = { HandModelKind.Camera,     HandModelKind.Controller, HandModelKind.Hand };
        private static readonly HandModelKind[] HandOrder         = { HandModelKind.Hand,       HandModelKind.Controller }; // 手押し相撲 / あ〜ん

        // index は手ごと（[0]=左 / [1]=右）。
        private readonly int[] m_normalIndex  = { 0, 0 }; // 通常選択（コンテキストでリセットしない）
        private readonly int[] m_contextIndex = { 0, 0 }; // 特殊コンテキスト内の選択（突入立上りで 0 リセット）
        private readonly HandModelContext[] m_prevContext = { HandModelContext.Normal, HandModelContext.Normal };

        /// <summary>このコンテキスト・このフレームの手元モデル種別を解決する（reset → cycle → get を一括）。
        /// cycleRequested=true で grip+トリガーの 1 回切替を反映する（同フレーム反映）。</summary>
        public HandModelKind Resolve(bool isLeft, HandModelContext ctx, bool cycleRequested)
        {
            int h = isLeft ? 0 : 1;

            // 突入リセット: 特殊コンテキストへ立ち上がった瞬間に既定（order 先頭）から開始する。
            // Normal は normalIndex 系統で contextIndex を触らない（第 2 条件 ctx != Normal で除外）。
            // Normal への戻りも prevContext に記録する（次の突入で立上りを検出するため）。
            if (ctx != m_prevContext[h] && ctx != HandModelContext.Normal)
                m_contextIndex[h] = 0;
            m_prevContext[h] = ctx;

            HandModelKind[] order = OrderFor(ctx, isLeft);
            bool useNormal = ctx == HandModelContext.Normal;

            if (cycleRequested)
            {
                if (useNormal) m_normalIndex[h]  = (m_normalIndex[h]  + 1) % order.Length;
                else           m_contextIndex[h] = (m_contextIndex[h] + 1) % order.Length;
            }

            // index は更新時に % order.Length 済み・コンテキスト変更時は 0 リセット済み＝常に order 範囲内
            // （特殊コンテキストの order 長変化は必ず突入リセットを伴うため index が溢れない）。
            return order[useNormal ? m_normalIndex[h] : m_contextIndex[h]];
        }

        private static HandModelKind[] OrderFor(HandModelContext ctx, bool isLeft)
        {
            switch (ctx)
            {
                case HandModelContext.Karaoke:  return isLeft ? KaraokeLeftOrder : KaraokeRightOrder;
                case HandModelContext.Cheki:    return ChekiOrder;
                case HandModelContext.HandSumo: return HandOrder;
                case HandModelContext.Ahhn:     return HandOrder;
                case HandModelContext.Drinking: return HandOrder; // Hand 既定→Controller（手押し相撲/あ〜んと同 order）
                default:                        return NormalOrder;
            }
        }
    }
}
