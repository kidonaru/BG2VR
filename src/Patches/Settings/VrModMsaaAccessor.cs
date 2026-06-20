using UnityVRMod.Config;

namespace BG2VR.Patches.Settings
{
    /// <summary>
    /// UnityVRMod の MSAA ConfigElement&lt;int&gt; を dropdown accessor として扱う。
    /// GetFloat は現在値→index、SetFloat は index→有効値を Value setter へ書く。
    /// Value setter 経由で BepInEx 永続化 + 次フレーム RenderEye が拾って live 反映する。
    /// </summary>
    internal sealed class VrModMsaaAccessor : IConfigAccessor
    {
        private readonly ConfigElement<int> m_element;

        public VrModMsaaAccessor(ConfigElement<int> element) { m_element = element; }

        public float GetFloat() => MsaaDropdownPolicy.IndexFromValue(m_element.Value);
        public void SetFloat(float v) => m_element.Value = MsaaDropdownPolicy.ValueFromIndex((int)System.Math.Round(v));
        public void ResetToDefault() => m_element.RevertToDefaultValue();
    }
}
