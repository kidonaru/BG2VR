namespace BG2VR.VrInput
{
    /// <summary>手元モデルの種別（per-hand 切替対象）。ControllerModelRunner が描画分岐を持つ。
    /// 手動 cycle（grip+トリガー）の対象は HandModelSelector.CycleOrder の固定集合のみ。
    /// Camera / Tambourine / GlowStick は手動 cycle 非対象＝ミニゲーム中の override 専用
    /// （ProjectorRunner が一時注入。Camera=Cheki / Tambourine+GlowStick=Karaoke）。</summary>
    public enum HandModelKind
    {
        Controller = 0,
        Hand = 1,
        Camera = 2,
        Tambourine = 3, // カラオケ左手プロップ（OBJ・テクスチャ無し＝submesh の Kd 色で描画）
        GlowStick = 4,  // カラオケ右手プロップ（サイリウム・色駆動 FBX Saliyum_Pink＝テクスチャ無し・パーツ色で描画）
    }
}
