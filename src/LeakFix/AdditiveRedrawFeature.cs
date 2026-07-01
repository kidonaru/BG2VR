using UnityEngine.Rendering.Universal;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// AdditiveRedrawPass を URP renderer に登録する ScriptableRendererFeature。
    /// ScriptableObject.CreateInstance で runtime 生成し、UrpFeatureInjector で注入する。
    /// </summary>
    internal sealed class AdditiveRedrawFeature : ScriptableRendererFeature
    {
        public override void Create() { }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!AdditiveRedrawPass.HasEntries) return;
            renderer.EnqueuePass(AdditiveRedrawPass.Instance);
        }
    }
}
