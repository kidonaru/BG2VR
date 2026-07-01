using System.Collections.Generic;
using BG2VR.Patches.Settings;
using UnityVRMod.Config;

namespace BG2VR.Config
{
    /// <summary>
    /// SettingsView へ注入する VR 調整行（UnityVRMod ConfigElement 直結のスライダー / トグル）。
    /// ConfigGen 生成の UIEntries には含めず（ConfigEntry でないため）、実行時に注入する。
    /// </summary>
    internal static class ComfortEntries
    {
        public static IReadOnlyList<UIEntryMeta> Build() => new[]
        {
            new UIEntryMeta
            {
                Category = "Comfort",
                Label = "World Scale",
                Desc = "世界の大きさ。>1 で大きく、<1 で小さく感じる。",
                Kind = UIKind.Slider,
                SliderMin = 0.1f, SliderMax = 3.0f, SliderStep = 0.1f, Format = "{0:F1}x",
                Accessor = new VrModFloatAccessor(ConfigManager.VrWorldScale),
            },
            new UIEntryMeta
            {
                Category = "Comfort",
                Label = "目の高さ(m)",
                Desc = "+ で高く / - で低く感じる（m）。",
                Kind = UIKind.Slider,
                SliderMin = -1.5f, SliderMax = 0.5f, SliderStep = 0.05f, Format = "{0:F2}m",
                Accessor = new VrModFloatAccessor(ConfigManager.VrUserEyeHeightOffset),
            },
            new UIEntryMeta
            {
                Category = "Comfort",
                Label = "デスクトップ描画OFF",
                Desc = "VR 描画中のみ、モニタのフラット描画を止めて GPU 負荷を下げる（モニタは暗転）。非描画中（Safe Mode 等）は自動で元に戻る。",
                Kind = UIKind.Toggle,
                Accessor = new VrModBoolAccessor(ConfigManager.DisableDesktopView),
            },
            new UIEntryMeta
            {
                Category = "Comfort",
                Label = "アンチエイリアス",
                Desc = "VR 映像の輪郭のジャギーを低減（MSAA）。高いほど滑らかだが GPU 負荷・VRAM 増。",
                Kind = UIKind.Dropdown,
                DropdownOptions = new[] { "オフ", "2x", "4x", "8x" },
                Accessor = new VrModMsaaAccessor(ConfigManager.VrEyeMsaa),
            },
            new UIEntryMeta
            {
                Category = "Comfort",
                Label = "GPU メモリ leak 修正",
                Desc = "環境物の material を per-renderer clone して D3D12 共有 GPU メモリ leak を防ぐ。" +
                       "見た目の変化なし。OFF にすると元の共有 material に戻るが leak が再開する。",
                Kind = UIKind.Toggle,
                Accessor = new VrModBoolAccessor(ConfigManager.OpenXR_LeakFixEnabled),
            },
        };
    }
}
