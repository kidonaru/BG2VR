namespace BG2VR.Utils;

/// <summary>
/// キーボードホットキーの修飾キー選択肢（Configs.yaml の enum dropdown 用）。
/// InputSystem の ctrlKey/altKey/shiftKey（左右統合の synthetic ButtonControl）に対応する。
/// None は持たない＝固定位置の保存/消去は必ず修飾キー併用とし単キー誤爆を防ぐ（設計判断）。
/// EnumAccessor&lt;T&gt; / DropdownOptions が global::BG2VR.Utils.KeyboardModifier を参照するため public。
/// </summary>
public enum KeyboardModifier
{
    // メンバ順は Ctrl を先頭に保つ＝EnumAccessor の未知値フォールバック(index0)・
    // IsModifierKeyboardTriggered の switch default がともに「既定 Ctrl」で一致する不変条件。
    Ctrl,
    Alt,
    Shift,
}
