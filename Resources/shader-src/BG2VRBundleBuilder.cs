using System.IO;
using UnityEditor;
using UnityEngine;

// BG2VR 同梱 shader / model を AssetBundle にビルドする。
// 実行: Unity メニュー「BG2VR/Build Shader Bundle」or batchmode -executeMethod BG2VRBundleBuilder.Build
public static class BG2VRBundleBuilder
{
    private const string ShaderPath = "Assets/BG2VRShaders/ControllerUnlit.shader";
    private const string UiAdditiveKeyedShaderPath = "Assets/BG2VRShaders/UiAdditiveKeyed.shader";
    private const string DepthOnlyShaderPath = "Assets/BG2VRShaders/DepthOnly.shader";
    private const string HandToonOverlayShaderPath = "Assets/BG2VRShaders/HandToonOverlay.shader";
    private const string ToonOutlineShaderPath = "Assets/BG2VRShaders/ToonOutline.shader";
    private const string AdditiveRedrawShaderPath = "Assets/BG2VRShaders/AdditiveRedraw.shader";
    private const string BundleName = "bg2vr_shaders";

    [MenuItem("BG2VR/Build Shader Bundle")]
    public static void Build()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "BundleOutput");
        Directory.CreateDirectory(outDir);

        var assetList = new System.Collections.Generic.List<string>
        {
            ShaderPath,
            UiAdditiveKeyedShaderPath,
            DepthOnlyShaderPath,
            HandToonOverlayShaderPath,
            ToonOutlineShaderPath,
            AdditiveRedrawShaderPath,
        };

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = assetList.ToArray(),
        };

        BuildPipeline.BuildAssetBundles(
            outDir,
            new[] { build },
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        Debug.Log($"[BG2VRBundleBuilder] built {BundleName} ({assetList.Count} asset) -> {outDir}");
    }

    // コントローラ render model（Meta Touch Plus）の Mesh+Texture bundle。
    private const string ModelBundleName = "bg2vr_models";
    private const string GlowStickFbxPath = "Assets/BG2VRModels/Saliyum_Pink.fbx";
    private static readonly string[] ModelAssets =
    {
        "Assets/BG2VRModels/MetaQuestTouchPlus_Left.fbx",
        "Assets/BG2VRModels/MetaQuestTouchPlus_Right.fbx",
        "Assets/BG2VRModels/MetaQuestTouchPlus_Left_BaseColor_AO.png",
        "Assets/BG2VRModels/MetaQuestTouchPlus_right_BaseColor_AO.png",
        "Assets/BG2VRModels/Player hand.fbx",
        "Assets/BG2VRModels/iphone model.fbx",
        "Assets/BG2VRModels/tamberine.obj",
        GlowStickFbxPath,
    };

    [MenuItem("BG2VR/Build Controller Model Bundle")]
    public static void BuildModels()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "BundleOutput");
        Directory.CreateDirectory(outDir);

        var build = new AssetBundleBuild
        {
            assetBundleName = ModelBundleName,
            assetNames = ModelAssets,
        };

        BuildPipeline.BuildAssetBundles(
            outDir,
            new[] { build },
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        foreach (var path in ModelAssets)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj is GameObject prefab)
            {
                Vector3 size = Vector3.zero;
                GameObject inst = null;
                try
                {
                    inst = Object.Instantiate(prefab);
                    bool first = true;
                    var bounds = new Bounds(Vector3.zero, Vector3.zero);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        if (first) { bounds = r.bounds; first = false; }
                        else bounds.Encapsulate(r.bounds);
                    }
                    size = bounds.size;
                    string pn = prefab.name.ToLowerInvariant();
                    if (pn.Contains("tamb") || pn.Contains("saliyum") || pn.Contains("iphone"))
                    {
                        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                            foreach (var m in r.sharedMaterials)
                                if (m != null)
                                {
                                    string col = m.HasProperty("_Color") ? m.color.ToString("F3")
                                        : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString("F3")
                                        : "(_Color/_BaseColor なし)";
                                    string emi = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor").ToString("F3")
                                        : "(emission なし)";
                                    Debug.Log($"[BG2VRBundleBuilder] color-driven submesh material '{m.name}' shader={m.shader.name} color={col} emission={emi}");
                                }
                    }
                }
                finally { if (inst != null) Object.DestroyImmediate(inst); }
                Debug.Log($"[BG2VRBundleBuilder] asset '{prefab.name}' (GameObject) native bounds size={size}");
            }
            else if (obj != null)
            {
                Debug.Log($"[BG2VRBundleBuilder] asset '{obj.name}' ({obj.GetType().Name})");
            }
        }

        Debug.Log($"[BG2VRBundleBuilder] built {ModelBundleName} -> {outDir}");
    }
}
