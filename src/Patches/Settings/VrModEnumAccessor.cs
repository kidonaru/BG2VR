using System;
using UnityVRMod.Config;

namespace BG2VR.Patches.Settings
{
    /// <summary>
    /// UnityVRMod の enum ConfigElement&lt;T&gt; を dropdown accessor として扱う。
    /// Enum.GetValues 上の index を float でやり取りする（EnumAccessor&lt;T&gt; for BepInEx ConfigEntry と同型）。
    /// 注: diagnostic 用途。enum config が将来増えるなら共通化候補。
    /// </summary>
    internal sealed class VrModEnumAccessor<T> : IConfigAccessor where T : struct, Enum
    {
        // Enum.GetValues と Enum.GetNames は同一順序（underlying value 昇順）で返るので
        // codegen 側 DropdownOptions の Enum.GetNames と index が一致する。
        private static readonly Array s_values = Enum.GetValues(typeof(T));
        private readonly ConfigElement<T> m_element;

        public VrModEnumAccessor(ConfigElement<T> element) { m_element = element; }

        public float GetFloat()
        {
            var idx = Array.IndexOf(s_values, m_element.Value);
            return idx < 0 ? 0f : idx;
        }

        public void SetFloat(float v)
        {
            if (s_values.Length == 0) return;
            var idx = (int)Math.Round(v);
            if (idx < 0) idx = 0;
            if (idx >= s_values.Length) idx = s_values.Length - 1;
            m_element.Value = (T)s_values.GetValue(idx);
        }

        public void ResetToDefault() => m_element.RevertToDefaultValue();
    }
}
