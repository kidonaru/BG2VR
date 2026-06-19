using HarmonyLib;
using GB;
using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// VR ボタン割当（ゲームパッド準拠）を GBInput getter へ橋渡しする（spec §4）。
    /// 意味論は**フレームスコープ**: ProjectorRunner.Update 先頭が前フレーム分を無条件に失効（Clear）し、
    /// 定常状態の VrGameButtonsRunner.Tick が今フレーム分をセットする。getter Postfix は読むだけで
    /// 消費しない（実機入力の InputAction.triggered と同じ＝同フレームの全読者に見える）。
    /// LeftClick（GbInputBridge）の read=consume とは意図的に異なる: read=consume は「休止 CW の消費
    /// Postfix がパルスを横取りする」という消費パッチ固有バグへの対症であり、消費レス設計では構造的に
    /// 発生しない。失効を Update 先頭（あらゆる早期 return より前）に置くのは、Tick が呼ばれない
    /// フレームにパルスが残灯して読者が同じパルスを 2 周期読むのを防ぐため（plan-review 指摘）。
    /// ナビは Pressing（レベル）も注入する＝ゲーム側 UpdateRepeat → *TriggeredR のリピート機構が
    /// 追加コードなしで効く（バックログスクロール等・spec §2.1）。
    /// </summary>
    internal static class GbGameButtonBridge
    {
        // メインスレッド（Update / Harmony Postfix）からのみ読み書きする前提。
        public static bool DecidePulse;    // 右 A → ATriggered（決定/会話送り）
        public static bool BackPulse;      // 右 B 短押し → BTriggered（戻る/キャンセル）
        public static bool StartPulse;     // 左メニューボタン → StartTriggered（ポーズメニュー開閉＝実 Start 等価）
        public static bool BacklogPulse;   // 左 X → XTriggered（バックログ表示）
        public static bool AutoPulse;      // スティック押し込み → AutoTriggered（オート切替）
        public static bool SkipHeld;       // 左 Y hold → YPressing（既読スキップ）
        public static bool NavUpPulse;     // 右スティック → Up/Down/Left/RightTriggered（エッジ）
        public static bool NavDownPulse;
        public static bool NavLeftPulse;
        public static bool NavRightPulse;
        public static bool NavUpHeld;      // 右スティック → Up/Down/Left/RightPressing（レベル・リピート供給源）
        public static bool NavDownHeld;
        public static bool NavLeftHeld;
        public static bool NavRightHeld;
        public static Vector2 RStickValue; // 左スティック → RStick（ミニゲームカメラ/アイテム回転）
        public static bool NotePulseZL;    // 左振り → ZLTriggered（タンバリン音符 = IsNoteInput）
        public static bool NotePulseZR;    // 右振り → ZRTriggered（ガヤ音符 = IsGayaInput）
        public static bool HandSumoPushPulse; // 両手前進 → ATriggered（手押し相撲のクリック・PLAYING 中のみ set）

        public static void Clear()
        {
            DecidePulse = false;
            BackPulse = false;
            StartPulse = false;
            BacklogPulse = false;
            AutoPulse = false;
            SkipHeld = false;
            NavUpPulse = false;
            NavDownPulse = false;
            NavLeftPulse = false;
            NavRightPulse = false;
            NavUpHeld = false;
            NavDownHeld = false;
            NavLeftHeld = false;
            NavRightHeld = false;
            RStickValue = Vector2.zero;
            NotePulseZL = false;
            NotePulseZR = false;
            HandSumoPushPulse = false;
        }

        /// <summary>元 getter の IsInputDisabled 抑制を尊重（遷移/fade/pause 中は注入しない）。</summary>
        internal static bool InputAllowed()
        {
            var gs = GBSystem.Instance;
            return gs == null || !gs.IsInputDisabled();
        }
    }

    /// <summary>右 A → 決定/会話送り。HarmonyX は class-level [HarmonyPatch] 必須。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.ATriggered), MethodType.Getter)]
    internal static class GBInput_ATriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            // DecidePulse=ゲームパッド右A / HandSumoPushPulse=手押し相撲の両手押し出し（PLAYING gate 下のみ set）。
            if ((GbGameButtonBridge.DecidePulse || GbGameButtonBridge.HandSumoPushPulse) && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>右 B 短押し → 戻る/キャンセル。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.BTriggered), MethodType.Getter)]
    internal static class GBInput_BTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.BackPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>左メニューボタン → ポーズメニュー開閉。StartTriggered=ESC/Start 相当を 1 フレーム注入＝
    /// 実 Start 押下と等価で、ゲーム各シーンの ShowPauseMenu 呼出ガード・即閉じ防止(0.2s 窓)・ミニゲーム中
    /// 抑止を全て再利用する。InputAllowed()(=fade/遷移/disable 窓中は抑止)も他ボタンと同一規約。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.StartTriggered), MethodType.Getter)]
    internal static class GBInput_StartTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.StartPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>左 X → バックログ表示。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.XTriggered), MethodType.Getter)]
    internal static class GBInput_XTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.BacklogPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>左 Y hold → 既読スキップ（レベル注入）。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.YPressing), MethodType.Getter)]
    internal static class GBInput_YPressing_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.SkipHeld && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>スティック押し込み → オート切替。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.AutoTriggered), MethodType.Getter)]
    internal static class GBInput_AutoTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.AutoPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    // ── 右スティック → ナビ（Triggered=エッジ / Pressing=レベル）──
    // 主経路は Pressing → UpdateRepeat → *TriggeredR（選択肢/ConfirmDialog/メニュー/バックログ等 102 箇所
    // が *TriggeredR 読み＝リピートは追加コード不要）。Triggered エッジは plain *Triggered 読者
    //（TutorialWindow/Album/ミニゲーム/コマンド列 32 箇所）向けの併設（spec §2.1）。

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.UpTriggered), MethodType.Getter)]
    internal static class GBInput_UpTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavUpPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.DownTriggered), MethodType.Getter)]
    internal static class GBInput_DownTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavDownPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.LeftTriggered), MethodType.Getter)]
    internal static class GBInput_LeftTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavLeftPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.RightTriggered), MethodType.Getter)]
    internal static class GBInput_RightTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavRightPulse && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.UpPressing), MethodType.Getter)]
    internal static class GBInput_UpPressing_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavUpHeld && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.DownPressing), MethodType.Getter)]
    internal static class GBInput_DownPressing_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavDownHeld && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.LeftPressing), MethodType.Getter)]
    internal static class GBInput_LeftPressing_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavLeftHeld && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    [HarmonyPatch(typeof(GBInput), nameof(GBInput.RightPressing), MethodType.Getter)]
    internal static class GBInput_RightPressing_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NavRightHeld && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>
    /// 左スティック → RStick（ミニゲームカメラ/アイテム回転）。非ゼロ時のみ上書き＝
    /// マウス経路（CameraControll の mouseCamera fallback）と実パッドを無傷に保つ。
    /// </summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.RStick), MethodType.Getter)]
    internal static class GBInput_RStick_Patch
    {
        private static void Postfix(ref Vector2 __result)
        {
            if (GbGameButtonBridge.RStickValue != Vector2.zero && GbGameButtonBridge.InputAllowed())
                __result = GbGameButtonBridge.RStickValue;
        }
    }

    /// <summary>カラオケ左振り → タンバリン音符（GBInput.IsNoteInput が ZLTriggered を読む）。
    /// pulse は KaraokeShakeRunner が IsKaraoke 中のみ set＝カラオケ外では常に false（Album 等へ漏れない）。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.ZLTriggered), MethodType.Getter)]
    internal static class GBInput_ZLTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NotePulseZL && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }

    /// <summary>カラオケ右振り → ガヤ音符（GBInput.IsGayaInput が ZRTriggered を読む）。</summary>
    [HarmonyPatch(typeof(GBInput), nameof(GBInput.ZRTriggered), MethodType.Getter)]
    internal static class GBInput_ZRTriggered_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (GbGameButtonBridge.NotePulseZR && GbGameButtonBridge.InputAllowed())
                __result = true;
        }
    }
}
