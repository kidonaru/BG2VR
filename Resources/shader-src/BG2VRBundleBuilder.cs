using System.IO;
using UnityEditor;
using UnityEngine;

// BG2VR コントローラモデル用 shader を AssetBundle にビルドする。
// 実行: Unity メニュー「BG2VR/Build Shader Bundle」or batchmode -executeMethod BG2VRBundleBuilder.Build
public static class BG2VRBundleBuilder
{
    private const string ShaderPath = "Assets/BG2VRShaders/ControllerUnlit.shader";
    private const string UiAdditiveKeyedShaderPath = "Assets/BG2VRShaders/UiAdditiveKeyed.shader";
    private const string DepthOnlyShaderPath = "Assets/BG2VRShaders/DepthOnly.shader";
    private const string BundleName = "bg2vr_shaders";

    [MenuItem("BG2VR/Build Shader Bundle")]
    public static void Build()
    {
        // プロジェクト直下の BundleOutput/ に出力（Assets 外＝再インポートされない）。
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "BundleOutput");
        Directory.CreateDirectory(outDir);

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { ShaderPath, UiAdditiveKeyedShaderPath, DepthOnlyShaderPath },
        };

        // ゲームは Windows x64 standalone。明示ターゲットでビルド。
        BuildPipeline.BuildAssetBundles(
            outDir,
            new[] { build },
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        Debug.Log($"[BG2VRBundleBuilder] built {BundleName} -> {outDir}");
    }

    // コントローラ render model（Meta Touch Plus）の Mesh+Texture bundle。
    // 明示 AssetBundleBuild で対象を限定（全タグ一括ビルドは bg2vr_shaders を巻き込むため使わない）。
    private const string ModelBundleName = "bg2vr_models";
    // カラオケ右手サイリウム FBX（自作 Saliyum_Pink）。テクスチャ無し＝マテリアル取込を残し、各パーツの Phong 色を runtime で unlit へ転写する。
    private const string GlowStickFbxPath = "Assets/BG2VRModels/Saliyum_Pink.fbx";
    private static readonly string[] ModelAssets =
    {
        "Assets/BG2VRModels/MetaQuestTouchPlus_Left.fbx",
        "Assets/BG2VRModels/MetaQuestTouchPlus_Right.fbx",
        "Assets/BG2VRModels/MetaQuestTouchPlus_Left_BaseColor_AO.png",
        "Assets/BG2VRModels/MetaQuestTouchPlus_right_BaseColor_AO.png",
        // VR 用手モデル（片手 FBX・CC-BY 4.0「Hand for VR」by FFeller）。埋め込みテクスチャは依存として自動同梱。
        // 別 PNG を使う場合はそのパスもここに追加する。
        "Assets/BG2VRModels/Player hand.fbx",
        // Cheki ミニゲーム用 iPhone X Lowpoly モデル（FBX・CC-BY 4.0 by FredDrabble）。
        // テクスチャ非同梱の色駆動モデル＝マテリアル取込を残し（None 非適用）、各 Phong パーツの
        // DiffuseColor を runtime で submesh ごとに unlit へ転写する（タンバリン/サイリウムと同経路）。
        "Assets/BG2VRModels/iphone model.fbx",
        // カラオケ左手タンバリン（OBJ・CC-BY 3.0「Tamborine」by Poly by Google）。テクスチャ無し＝
        // マテリアル取込を残し（既定）、4 つの Kd フラット色を runtime で submesh ごとに unlit へコピーする。
        "Assets/BG2VRModels/tamberine.obj",
        // カラオケ右手サイリウム（自作 FBX Saliyum_Pink・5 パーツ／5 Phong マテリアル・テクスチャ無し）。
        // タンバリンと同じ色駆動プロップ＝マテリアル取込を残す（None 非適用）。各パーツの DiffuseColor を runtime で unlit へ転写。
        GlowStickFbxPath,
    };

    [MenuItem("BG2VR/Build Controller Model Bundle")]
    public static void BuildModels()
    {
        string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "BundleOutput");
        Directory.CreateDirectory(outDir);

        // 手/コントローラ FBX はマテリアル取込のまま（albedo を bundle 内テクスチャから解決する既存方式）。
        // タンバリン OBJ / サイリウム FBX / iPhone FBX は色駆動＝マテリアル取込を残す（各パーツ色を runtime で submesh から読むため・None 非適用）。
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

        // 診断: 同梱予定アセットの実名と GameObject の native bounds を出力。
        // name 引き（loader の ResolveByName）の妥当性確認 + カメラ base scale 算出根拠（実寸/native size）。
        foreach (var path in ModelAssets)
        {
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj is GameObject prefab)
            {
                Vector3 size = Vector3.zero;
                GameObject inst = null;
                try
                {
                    inst = Object.Instantiate(prefab); // 原点・prefab スケールで実体化＝runtime と同じ native 寸法
                    bool first = true;
                    var bounds = new Bounds(Vector3.zero, Vector3.zero);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        if (first) { bounds = r.bounds; first = false; }
                        else bounds.Encapsulate(r.bounds);
                    }
                    size = bounds.size;
                    // 色駆動プロップ（タンバリン/サイリウム）はテクスチャ無し＝submesh のマテリアル色を runtime で読む。
                    // bake 後も `.color`(_Color/_BaseColor) が取れるか・灰や near-black に落ちていないかを確証する。
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
