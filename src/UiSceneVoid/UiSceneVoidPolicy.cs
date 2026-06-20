using System;
using System.Collections.Generic;

namespace BG2VR.UiSceneVoid
{
    /// <summary>
    /// active env scene の種別（純 enum）。EnvSceneBase からのマップは EnvKindClassifier（ゲーム型依存）。
    /// 分類は C# 型ベース＝ゲームの EnvSceneType enum は見ない（EnvSceneType に Talk2D 値は存在せず
    /// Afternoon/Evening 等の env asset に化けるため、enum での分類は構造的に不可能。plan §6）。
    /// </summary>
    public enum EnvKind
    {
        None,       // env なし（boot 過渡等）
        Hole,       // Bar 店内（HoleScene・常駐 SetActive 切替）
        Talk2D,     // 2D 背景イベント舞台（Talk2DScene・Afternoon/Evening 等の env asset）
        SteelFrame, // 鉄骨ミニゲーム（SteelFrameScene）
        Other,      // VipRoom / GameRoom / 未知型 / debug シーン
    }

    /// <summary>
    /// UI-only 画面 void 化の判定規則（純関数・UnityEngine 非依存）。
    ///
    /// ゲームは env scene（Bar 店内 / デート後の Talk2D 舞台等の 3D）を UI 画面で隠さず、
    /// フルスクリーン不透明 UI で覆い隠すだけ（Hole は常駐 SetActive 切替・他 env も遷移まで残留）。
    /// フラット画面では見えないが、VR では投影 quad の裏に 3D が丸見えになる。
    /// → シーン名の 2 層分類 × env 種別で void を判定する（plan §6 v2）。
    /// </summary>
    public static class UiSceneVoidPolicy
    {
        /// <summary>
        /// フルスクリーン UI 専用画面（メニュー）。意図的に 3D を見せるのは HomeScene の
        /// 鉄骨ミニゲーム（env=SteelFrame）のみ＝それ以外の env は全て残留であり void してよい
        /// （ShowTalk2DScene 全呼出元 / ShowSteelFrameScene 呼出元 grep で確認。plan §6）。
        /// </summary>
        private static readonly HashSet<string> MenuScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "HomeScene",
            "TitleScene",
            "ExtraScene",
            "StaffCreditScene",
            "FirstScene",
        };

        /// <summary>
        /// Talk2D を上演する UI シーン。「上演中」と「残留」が (シーン名, env 種別) では区別不能のため、
        /// 確実に leak と言える Hole（bar gameplay 専用 env の惰性残留）のみ void する保守側ルール。
        /// </summary>
        private static readonly HashSet<string> EventScenes = new HashSet<string>(StringComparer.Ordinal)
        {
            "AfterScene",
            "HolidayAfterScene",
            "WeekdayEncountScene",
            "PrologueScene",
            "EpilogueScene",
        };

        /// <summary>
        /// void すべきか。照合対象は Scene.name（= GBSystem.GetCurrentSceneName()）。
        /// null/empty（遷移過渡）・未知シーン名（BarScene / EscortedEntryScene / 将来 DLC 等）は
        /// void しない側に倒す（3D を誤って隠さない安全側）。
        /// </summary>
        public static bool ShouldVoid(string sceneName, EnvKind env, bool miniGameStages3D)
        {
            // 3D を上演するミニゲーム進行中はシーン名が menu のままでも 3D が主役＝隠さない
            // （エクストラ発カラオケが ExtraScene+VipRoom(Other) で void され真っ黒になった実バグ
            //   2026-06-07。「残留」と「上演」は (シーン名, env 種別) では区別できず、
            //   MiniGameBase.s_instance の activeness が唯一の判別子）。
            // 2D 演出のミニゲーム（ASMR）は probe 側で除外され void 維持（黒背景が正）。
            if (miniGameStages3D) return false;
            if (string.IsNullOrEmpty(sceneName)) return false;
            if (MenuScenes.Contains(sceneName)) return env != EnvKind.SteelFrame;
            if (EventScenes.Contains(sceneName)) return env == EnvKind.Hole;
            return false;
        }
    }
}
