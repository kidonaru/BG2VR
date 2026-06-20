using HarmonyLib;
using GB;

namespace BG2VR.VrInput
{
    /// <summary>
    /// VR レーザーの「画面クリック」を GBInput.LeftClick へ橋渡しする。
    /// 会計（Bill.waitInput）・会話送り（ConversationWindow.UpdateRoutine）等、ゲームの「クリックで進む」
    /// 待ち UI は GBInput.LeftClick を直接ポーリングするため、uGUI イベント注入（ボタン類はこちらで届く）
    /// では届かない。非インタラクティブ領域のトリガークリックを 1 フレーム限りのパルスにして橋渡しする。
    ///
    /// 消費は read=consume（下記 getter Postfix が強制 true にした瞬間にパルスをクリア）。
    /// 旧方式（ConversationWindow.UpdateRoutine Postfix での消費 + 会話中 gate）は、UIManager.GBUpdate が
    /// 休止中の ConversationWindow にも毎フレ UpdateRoutine を呼ぶため、会話外（会計表示中等）は
    /// 「本体は m_isDone 冒頭 return で LeftClick を読まないのに Postfix だけが毎フレ走ってパルスを横取り
    /// 消費する」実バグになった（2026-06-04 BG2DevBridge で実観測）。read=consume は最初の読者が
    /// 1 回だけ消費するため実行順非依存で exactly-once が成立し、gate も消費パッチも不要になる。
    /// GBInput.LeftClick の読者は全 18 箇所すべて「クリックで進む/決定」型のモーダル待ちで、
    /// 同時待ちは逐次モーダル設計上実質発生しない（万一同時でも「もう 1 クリック必要」に縮退するだけ）。
    /// </summary>
    internal static class GbInputBridge
    {
        // メインスレッド（Update / Harmony Postfix）からのみ読み書きする前提（VR/ゲーム入力経路は single-thread）。
        // クリック要求パルス。VrPointerRunner.Tick が立て、最初の GBInput.LeftClick 読者が消費（read=consume）。
        // 誰も読まなかった場合は次フレームの Tick 冒頭で失効（寿命 ≦ 1 フレームサイクル＝実マウスと同じ
        // 「誰も聞いていない時のクリックは捨てる」挙動）。
        public static bool LeftClickPulse;
    }

    /// <summary>
    /// GBInput.LeftClick getter を強制 true（パルス時・入力無効中を除く）+ その場でパルス消費。
    /// HarmonyX は class-level [HarmonyPatch] 必須。
    /// </summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.LeftClick), MethodType.Getter)]
    internal static class GBInput_LeftClick_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (!GbInputBridge.LeftClickPulse) return;
            // 元 getter の IsInputDisabled 抑制を尊重（遷移/fade/pause 中は送らず、消費もしない＝Tick 失効に委ねる）。
            var gs = GBSystem.Instance;
            if (gs == null || !gs.IsInputDisabled())
            {
                __result = true;
                GbInputBridge.LeftClickPulse = false; // read=consume（最初の読者が 1 回だけ消費）
            }
        }
    }
}
