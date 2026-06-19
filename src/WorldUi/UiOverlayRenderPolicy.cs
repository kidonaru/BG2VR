using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>UI オーバーレイ視覚物の種別（EffectiveDepthTest の遮蔽挙動を分ける）。</summary>
    internal enum UiOverlayKind
    {
        Panel,    // ゲーム UI パネル
        Button,   // パネル下の調整ボタン帯
        Laser,    // ポインタのレーザー線
        Reticle,  // ポインタのヒットレティクル
        Settings, // VR 設定 modal パネル（常に最前面）
    }

    /// <summary>
    /// UI オーバーレイ（パネル/調整ボタン/レーザー）の描画順・ZTest と、透明 RT 合成を壊す
    /// ゲーム側 shader の検出を集約する純ロジック。
    /// 背景（実測 2026-06-07・BG2DevBridge）: ゲームは built-in pipeline で Unlit 系 shader が
    /// strip 済み。髪 Toon shader は multi-pass（base q=2450 / hairshadow q=3006 / 前髪 over-pass
    /// q=3010）のため、UI を transparent 既定の q=3000 に置くと前髪が UI を上書きする。
    /// </summary>
    internal static class UiOverlayRenderPolicy
    {
        /// <summary>パネル quad の renderQueue（RenderQueue.Overlay。髪 over-pass 最大 3010 より上）。</summary>
        public const int PanelQueue = 4000;

        /// <summary>調整ボタンの renderQueue（ZWrite Off 同士は queue 順＝描画順。パネルより常に後）。</summary>
        public const int ButtonQueue = 4001;

        /// <summary>レーザー/レティクルの renderQueue（ポインタは最前面）。</summary>
        public const int LaserQueue = 4002;

        /// <summary>
        /// VR 設定パネル合成オーバーレイの renderQueue（ゲーム UI パネル PanelQueue より上＝前面合成）。
        /// 設定 quad は modal 中のみ存在し、その間は laser(4002)/buttons(4001) が suppress され共存しないため、
        /// gamePanel(4000) より上であればよい。depthTest=false(ZTest Always) と併用し曲面でも端の z-fighting を回避。
        /// </summary>
        public const int SettingsOverlayQueue = 4005;

        /// <summary>設定パネルのレーザー/レティクル。設定オーバーレイ(4005)より前面に描くため最上位。</summary>
        public const int SettingsLaserQueue = 4006;

        /// <summary>
        /// UI/Default の ZTest を制御する shader 変数名。Properties 未宣言の変数のため
        /// HasProperty は False を返すが SetInt は機能する（実測 2026-06-07）＝fallback 検出には
        /// 使えない。shader 名比較で判定すること。
        /// </summary>
        public const string GuiZTestProperty = "unity_GUIZTestMode";

        /// <summary>
        /// depth test 設定 → UI/Default の unity_GUIZTestMode 値（CompareFunction）。
        /// false（既定）= Always(8)＝常に最前面。true = 深度テスト有効＝手前の物体に隠れる。
        /// 比較方向は depth buffer の向きに依存する: reversedZ（D3D 等＝near が大きい値）では GEqual(7)、
        /// 非 reversed では LEqual(4)。VR eye RT は usesReversedZBuffer に従う（fork の custom 投影も
        /// 同 platform 規約で描かれ、occluder 深度カーブもこの buffer 上で行うため一致が要る）。
        /// </summary>
        public static int ZTestMode(bool depthTest, bool reversedZ) => depthTest ? (reversedZ ? 7 : 4) : 8;

        /// <summary>
        /// 種別ごとの実効 depth test を解決する純関数（コントローラ遮蔽 VrControllerOccludeUi の核）。
        /// occludeUi=true（遮蔽 ON）のとき:
        ///  - パネル/ボタン → true(LEqual)＝コントローラ(深度カーブ後の遮蔽源)に隠れる。
        ///  - レーザー/レティクル → false(Always)＝ポインタは常に最前面（遮蔽されない）。
        /// occludeUi=false（遮蔽 OFF）のときは従来どおり worldDepthTest（WorldUiDepthTest）に従う。
        /// 設定 modal パネルは occludeUi に依らず常に false(Always)＝最前面。
        /// </summary>
        public static bool EffectiveDepthTest(UiOverlayKind kind, bool occludeUi, bool worldDepthTest)
        {
            switch (kind)
            {
                case UiOverlayKind.Settings:
                    return false; // modal は常に最前面（occlude 非適用）
                case UiOverlayKind.Reticle:
                    return occludeUi ? false : worldDepthTest; // レティクルはヒット点（パネル面）に常に最前面
                // 新種別を足すときは明示 case を追加すること（default はパネル挙動＝遮蔽 ON で隠れる側に合流する。
                // 最前面であるべきポインタ系を足して case を忘れると意図せず隠れる）。
                case UiOverlayKind.Panel:
                case UiOverlayKind.Button:
                case UiOverlayKind.Laser:   // レーザーも遮蔽 ON でコントローラに隠れる（貫通しない。カーブはコントローラのみ＝シーンには隠れない）
                default:
                    return occludeUi ? true : worldDepthTest;   // 遮蔽 ON でコントローラに隠れる（depth test）
            }
        }

        /// <summary>
        /// 透明 RT の dst alpha を乗算で潰す（＝world パネル上で UI に穴を開ける）ゲーム側 shader か。
        /// 該当: "UI/Mulatiply"（ゲーム側の typo がそのままの shader 名。GBSystem の Footer /
        /// LocationUI の暗化帯が使用）。
        /// </summary>
        public static bool IsAlphaEatingShader(string shaderName) =>
            shaderName != null && shaderName.Contains("Mulatiply");

        /// <summary>
        /// 透明 RT に加算で描かれ straight-alpha 合成で黒矩形化するゲーム側 shader か。
        /// 該当: "UI/AddBlend"（加算・グロー形状が RGB／alpha 不透明のため黒背景が不透明合成される）。
        /// 輝度キー材質（BG2VR/UiAdditiveKeyed）へ差し替えて黒背景を透明化する（ManagedCanvas 参照）。
        /// </summary>
        public static bool IsAdditiveShader(string shaderName) =>
            shaderName != null && shaderName.Contains("AddBlend");

        /// <summary>
        /// 乗算帯の代替表示色（UI/Default + この tint）。
        /// 実測 2026-06-07 (BG2DevBridge): 乗算帯が RT の dst alpha を 0.231→0.078 に潰す。
        /// 本代替（一様半透明黒）で 0.365 に復元・実機目視 OK。調整要望が出たら Configs 化。
        /// </summary>
        public static readonly Color AlphaEatingReplacementColor = new Color(0f, 0f, 0f, 0.45f);

        /// <summary>
        /// UI オーバーレイ用 material へ queue と ZTest を適用する。
        /// UI/Default 以外（Sprites/Default fallback）では SetInt は黙って無視され
        /// ZTest LEqual 固定の機能劣化のみ（呼び出し側が fallback 時に LogWarning する規約）。
        /// </summary>
        public static void Apply(Material material, int queue, bool depthTest)
        {
            if (material == null) return;
            material.renderQueue = queue;
            material.SetInt(GuiZTestProperty, ZTestMode(depthTest, SystemInfo.usesReversedZBuffer));
        }
    }
}
