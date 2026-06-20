namespace BG2VR.VrInput
{
    /// <summary>donor 候補マテリアルの shader 名が採用可能か判定する純関数。Unity API 非依存＝テスト host で動作可。
    /// 拒否条件: null / 空文字列 / `Hidden/InternalErrorShader`（Addressables 遷移中の placeholder shader）。</summary>
    internal static class HandSkinShaderClassifier
    {
        private const string ErrorShaderName = "Hidden/InternalErrorShader";

        public static bool IsAcceptable(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;
            if (shaderName == ErrorShaderName) return false;
            return true;
        }
    }
}
