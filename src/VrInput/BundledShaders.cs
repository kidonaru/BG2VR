using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// BG2VR に同梱した AssetBundle から shader をロードする（一度だけ・キャッシュ）。
    /// ゲーム本体には適切な shader が無い（lit は VR eye で真っ黒・unlit 既製は ZWrite Off で solid 不可・
    /// UI/AddBlend は透明 RT 合成で黒矩形化）ため、BG2VR.Unity でビルドした shader を埋め込む。
    /// bundle 名 = BG2VR.Resources.bg2vr_shaders。複数 shader を shader.name で引く（誤選択防止）。
    /// </summary>
    internal static class BundledShaders
    {
        private const string ResourceSuffix = "bg2vr_shaders";
        private const string ControllerUnlitName = "BG2VR/ControllerUnlit";
        private const string UiAdditiveKeyedName = "BG2VR/UiAdditiveKeyed";
        private const string DepthOnlyName = "BG2VR/DepthOnly";

        private static bool s_loaded;
        private static readonly Dictionary<string, Shader> s_byName = new Dictionary<string, Shader>();

        /// <summary>VR コントローラ render model 用 unlit-opaque-textured shader。失敗時 null（呼び出し側 fallback）。</summary>
        public static Shader ControllerUnlit => Get(ControllerUnlitName);

        /// <summary>加算 UI エフェクト補正用 輝度キー alpha shader（rgb 維持・alpha=輝度×color.a）。失敗時 null。</summary>
        public static Shader UiAdditiveKeyed => Get(UiAdditiveKeyedName);

        /// <summary>選択的深度プリパス用 深度のみ書き込み shader（ColorMask 0・ZWrite On）。失敗時 null（コントローラ遮蔽 OFF）。</summary>
        public static Shader DepthOnly => Get(DepthOnlyName);

        private static Shader Get(string name)
        {
            EnsureLoaded();
            return s_byName.TryGetValue(name, out var sh) ? sh : null;
        }

        // 初回のみ bundle を展開し、supported な shader を name で辞書化（失敗も含め再試行しない＝決定的）。
        private static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string resName = null;
                foreach (var n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith(ResourceSuffix)) { resName = n; break; }
                }
                if (resName == null)
                {
                    Plugin.Log.LogWarning("[BundledShaders] 同梱 shader bundle が見つからない。");
                    return;
                }
                byte[] data;
                using (var s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null)
                    {
                        Plugin.Log.LogWarning("[BundledShaders] shader bundle リソースストリームが取得できない。");
                        return;
                    }
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        data = ms.ToArray();
                    }
                }
                // LoadFromMemory は同期・メインスレッド前提。bundle は Unload しない（shader を生かし続ける）。
                AssetBundle bundle = AssetBundle.LoadFromMemory(data);
                if (bundle == null)
                {
                    Plugin.Log.LogWarning("[BundledShaders] shader bundle のロードに失敗。");
                    return;
                }
                // LoadAsset は shader 内部名で引けないため全件取得し name で辞書化（実行環境で非サポートは除外）。
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                if (shaders != null)
                {
                    foreach (var sh in shaders)
                    {
                        if (sh != null && sh.isSupported && !s_byName.ContainsKey(sh.name))
                            s_byName[sh.name] = sh;
                    }
                }
                if (s_byName.Count == 0)
                    Plugin.Log.LogWarning("[BundledShaders] bundle に利用可能な Shader が無い（非サポート/0 件）。");
                else
                    // bake 部分失敗（2 枚目が落ちる無音欠落）を実機ログで検出できるよう、件数と名前を必ず出す。
                    Plugin.Log.LogInfo($"[BundledShaders] {s_byName.Count} shader ロード: {string.Join(", ", new List<string>(s_byName.Keys).ToArray())}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[BundledShaders] shader bundle ロード例外: {e.Message}");
            }
        }
    }
}
