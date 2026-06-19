using BepInEx.Configuration;
using BG2VR.Patches.Settings;
using UnityEngine.InputSystem;

#nullable enable

namespace BG2VR.Utils;

public class HotkeyConfig
{
    public ConfigEntry<Key>? KeyConfig { get; }
    public ConfigEntry<ControllerButton>? ButtonConfig { get; }

    public HotkeyConfig(
        ConfigFile config,
        string section,
        string key,
        Key defaultKey,
        ControllerButton defaultButton,
        string label,
        string description,
        string controllerDescription = "")
    {
        KeyConfig = config.Bind(section, KeyboardKey(key), defaultKey, BuildDescription(label, description, "Keyboard", null));
        ButtonConfig = config.Bind(section, GamepadKey(key), defaultButton, BuildDescription(label, description, "Gamepad", controllerDescription));
    }

    private static string BuildDescription(string label, string description, string suffix, string? extra)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(label).Append(" (").Append(suffix).Append(')');
        if (!string.IsNullOrEmpty(description)) sb.Append('\n').Append(description);
        if (!string.IsNullOrEmpty(extra)) sb.Append('\n').Append(extra);
        return sb.ToString();
    }

    public static string GamepadKey(string key) => $"{key}Button";

    public static string KeyboardKey(string key) => $"{key}Key";

    public override string ToString()
    {
        string? keyLabel = KeyConfig == null || KeyConfig.Value == Key.None ? null : KeyConfig.Value.ToString();
        // BG2VR は gamepad combo 修飾（ControllerModifier）の Config を持たないため None 固定で中和。
        // BG2VR には hotkey エントリが無く本経路は死コード。型解決のみ満たす。
        string? buttonLabel = GetControllerBindingLabel(ControllerButton.None, ButtonConfig?.Value);

        if (keyLabel != null && buttonLabel != null)
            return $"{keyLabel} / {buttonLabel}";

        return keyLabel ?? buttonLabel ?? "Unbound";
    }

    public bool IsHeld()
    {
        // キーバインドキャプチャ中 / キャプチャ確定直後 (SuppressGameInput 期間) は押下判定を無効化。
        // 確定したキーが同フレームでホットキーとして発火する誤動作を防ぐ。
        if (SettingsController.ShouldSuppressHotkey()) return false;

        if (KeyConfig != null && Keyboard.current?[KeyConfig.Value].isPressed == true)
            return true;

        // ControllerModifier は None 固定で中和（BG2VR は gamepad combo 修飾 Config 無し・死コード）。
        if (ButtonConfig != null && GamepadHelper.IsHeld(ControllerButton.None) &&
            GamepadHelper.IsHeld(ButtonConfig.Value))
        {
            return true;
        }

        return false;
    }

    public bool IsTriggered()
    {
        // キーバインドキャプチャ中 / キャプチャ確定直後 (SuppressGameInput 期間) は押下判定を無効化
        if (SettingsController.ShouldSuppressHotkey()) return false;
        return IsKeyboardTriggered() || IsControllerTriggered();
    }

    public bool IsKeyboardTriggered()
    {
        // キーバインドキャプチャ中 / キャプチャ確定直後 (SuppressGameInput 期間) は押下判定を無効化
        if (SettingsController.ShouldSuppressHotkey()) return false;
        return KeyConfig != null && Keyboard.current?[KeyConfig.Value].wasPressedThisFrame == true;
    }

    /// <summary>
    /// 指定の修飾キー（Ctrl/Alt/Shift・左右どちらか）を押しながら設定キーが押された瞬間に true。
    /// 単キーが他 Mod と競合するため、誤爆しにくい修飾キー併用の保存/消去系で使う。
    /// 修飾キーは呼び出し側の Config（例 PinnedPoseModifier）で選択（キー自体は cfg/F9 で再設定可）。
    /// InputSystem の ctrlKey/altKey/shiftKey は左右の修飾キーを統合した synthetic ButtonControl。
    /// </summary>
    public bool IsModifierKeyboardTriggered(KeyboardModifier modifier)
    {
        if (SettingsController.ShouldSuppressHotkey()) return false;
        var kb = Keyboard.current;
        if (KeyConfig == null || kb == null) return false;

        // 未知値（.cfg 手編集等）は安全側で Ctrl にフォールバック。
        var modControl = modifier switch
        {
            KeyboardModifier.Alt   => kb.altKey,
            KeyboardModifier.Shift => kb.shiftKey,
            _                      => kb.ctrlKey,
        };
        return modControl.isPressed && kb[KeyConfig.Value].wasPressedThisFrame;
    }

    public bool IsControllerTriggered()
    {
        // キーバインドキャプチャ中 / キャプチャ確定直後 (SuppressGameInput 期間) は押下判定を無効化
        if (SettingsController.ShouldSuppressHotkey()) return false;

        // ControllerModifier は None 固定で中和（死コード）。Plugin 入力抑制も BG2VR 側の no-op を呼ぶ。
        if (ButtonConfig != null &&
            IsControllerComboTriggered(ControllerButton.None, ButtonConfig.Value))
        {
            global::BG2VR.Plugin.SuppressGameInputTemporarily();
            return true;
        }

        return false;
    }

    private static string? GetControllerBindingLabel(ControllerButton modifier, ControllerButton? action)
    {
        if (action == null || action == ControllerButton.None)
            return null;

        if (modifier == ControllerButton.None || modifier == action)
            return action.ToString();

        return $"{modifier}+{action}";
    }

    private static bool IsControllerComboTriggered(ControllerButton modifier, ControllerButton action)
    {
        if (action == ControllerButton.None)
            return false;

        if (modifier == ControllerButton.None || modifier == action)
            return GamepadHelper.IsTriggered(action);

        return GamepadHelper.IsHeld(modifier) && GamepadHelper.IsTriggered(action);
    }
}
