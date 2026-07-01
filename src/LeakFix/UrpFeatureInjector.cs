using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BG2VR.LeakFix
{
    /// <summary>
    /// URP renderer の内部 feature リストに ScriptableRendererFeature を runtime 注入/除去する。
    /// renderer は scene load 等で再構築されるため、EnsureInjected を毎フレ呼んで
    /// feature が消えていれば再注入する。
    /// </summary>
    internal static class UrpFeatureInjector
    {
        private static FieldInfo s_rendererFeaturesField;
        private static bool s_fieldResolved;
        private static ScriptableRendererFeature s_feature;
        private static bool s_loggedOnce;

        internal static bool IsInjected { get; private set; }

        private static FieldInfo GetRendererFeaturesField()
        {
            if (s_fieldResolved) return s_rendererFeaturesField;
            s_fieldResolved = true;
            s_rendererFeaturesField = typeof(ScriptableRenderer).GetField(
                "m_RendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);
            if (s_rendererFeaturesField == null)
                Plugin.Log.LogWarning("[UrpFeatureInjector] m_RendererFeatures フィールド未検出");
            return s_rendererFeaturesField;
        }

        /// <summary>
        /// feature を登録する。実際の注入は EnsureInjected で毎フレ行う。
        /// </summary>
        internal static bool Register(ScriptableRendererFeature feature)
        {
            var fi = GetRendererFeaturesField();
            if (fi == null) return false;
            s_feature = feature;
            s_feature.Create();
            s_loggedOnce = false;
            return EnsureInjected();
        }

        /// <summary>
        /// 毎フレ呼ぶ。renderer の feature list に feature が含まれていなければ再注入する。
        /// renderer 再構築で list がリセットされるケースを補償する。
        /// </summary>
        internal static bool EnsureInjected()
        {
            if (s_feature == null) return false;

            var fi = GetRendererFeaturesField();
            if (fi == null) return false;

            var renderer = ResolveRenderer();
            if (renderer == null) { IsInjected = false; return false; }

            var features = fi.GetValue(renderer) as IList<ScriptableRendererFeature>;
            if (features == null) { IsInjected = false; return false; }

            if (features.Contains(s_feature))
            {
                IsInjected = true;
                return true;
            }

            features.Add(s_feature);
            IsInjected = true;
            if (!s_loggedOnce)
            {
                Plugin.Log.LogInfo($"[UrpFeatureInjector] feature 注入成功 (features count={features.Count})");
                s_loggedOnce = true;
            }
            return true;
        }

        internal static void Unregister()
        {
            if (s_feature == null) return;

            var fi = GetRendererFeaturesField();
            if (fi != null)
            {
                var renderer = ResolveRenderer();
                if (renderer != null)
                {
                    var features = fi.GetValue(renderer) as IList<ScriptableRendererFeature>;
                    features?.Remove(s_feature);
                }
            }

            s_feature = null;
            IsInjected = false;
        }

        private static ScriptableRenderer ResolveRenderer()
        {
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null) return null;

            try
            {
                var renderer = urpAsset.scriptableRenderer;
                if (renderer != null) return renderer;
            }
            catch { }

            return ResolveRendererViaReflection(urpAsset);
        }

        private static ScriptableRenderer ResolveRendererViaReflection(UniversalRenderPipelineAsset urpAsset)
        {
            const BindingFlags nonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

            var renderersField = typeof(UniversalRenderPipelineAsset).GetField("m_Renderers", nonPublic);
            if (renderersField == null) return null;

            var renderers = renderersField.GetValue(urpAsset) as ScriptableRenderer[];
            if (renderers == null || renderers.Length == 0) return null;

            int defaultIndex = 0;
            var indexField = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", nonPublic);
            if (indexField != null)
                defaultIndex = (int)indexField.GetValue(urpAsset);

            if (defaultIndex < 0 || defaultIndex >= renderers.Length) return null;
            return renderers[defaultIndex];
        }
    }
}
