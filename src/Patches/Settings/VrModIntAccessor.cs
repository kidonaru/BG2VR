using UnityVRMod.Config;

namespace BG2VR.Patches.Settings
{
    /// <summary>
    /// UnityVRMod の ConfigElement&lt;int&gt; を UIEntry の accessor として扱う。
    /// comfort 値は BepInEx ConfigEntry でなく UnityVRMod cfg を単一情報源とするためのブリッジ。
    /// SetFloat が Mathf.RoundToInt → Value setter → OnValueChanged 経由で LiveUpdate* を呼び即時反映する。
    /// </summary>
    internal sealed class VrModIntAccessor : IConfigAccessor
    {
        private readonly ConfigElement<int> m_element;

        public VrModIntAccessor(ConfigElement<int> element) { m_element = element; }

        public float GetFloat() => m_element.Value;
        public void SetFloat(float v) => m_element.Value = UnityEngine.Mathf.RoundToInt(v);
        // ConfigElement 既存の RevertToDefaultValue() を使う（DefaultValue は object 型・キャスト不要で堅牢）。
        public void ResetToDefault() => m_element.RevertToDefaultValue();
    }
}
