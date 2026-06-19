using UnityVRMod.Config;

namespace BG2VR.Patches.Settings
{
    /// <summary>
    /// UnityVRMod の ConfigElement&lt;bool&gt; を UIEntry の accessor として扱う（Toggle 用・0/1）。
    /// SetFloat が Value setter → OnValueChanged 経由で fork 側の即時反映（LiveUpdateDesktopView）を呼ぶ。
    /// fork の ConfigElement を単一情報源にするためのブリッジ（BepInEx ConfigEntry を複製しない）。
    /// </summary>
    internal sealed class VrModBoolAccessor : IConfigAccessor
    {
        private readonly ConfigElement<bool> m_element;

        public VrModBoolAccessor(ConfigElement<bool> element) { m_element = element; }

        public float GetFloat() => m_element.Value ? 1f : 0f;
        public void SetFloat(float v) => m_element.Value = v >= 0.5f;
        public void ResetToDefault() => m_element.RevertToDefaultValue();
    }
}
