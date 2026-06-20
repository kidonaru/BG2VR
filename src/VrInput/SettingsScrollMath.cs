namespace BG2VR.VrInput
{
    /// <summary>
    /// 左スティック縦 → 設定パネル(F10)のスクロール量(px)変換の純ロジック（System のみ・テスト可）。
    /// 依存最小化のため Mathf は使わず手書きの float 比較で deadzone 判定する（既存 StickMoveSolver 踏襲）。
    /// Configs は読まず解決済み値を引数で受ける（既存純関数規約）。
    /// </summary>
    public static class SettingsScrollMath
    {
        /// <summary>
        /// stickY(-1..1, 上が正) を 1 フレームのスクロール delta[px] へ変換する。
        /// |stickY| が deadzone 未満は 0（ドリフト無視）。
        /// 戻り値 &gt; 0 で scrollOffset を増やす方向（＝下スクロール・下の項目を表示）。
        /// 上倒し(stickY &gt; 0)は負を返す（ホイール上＝上スクロール）。
        /// </summary>
        public static float Delta(float stickY, float deadzone, float speed, float dt)
        {
            if (stickY > -deadzone && stickY < deadzone) return 0f; // deadzone 内＝無入力
            return -stickY * speed * dt;
        }
    }
}
