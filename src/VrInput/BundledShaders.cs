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
        private const string HandToonOverlayName = "BG2VR/HandToonOverlay";
        private const string ToonOutlineName = "BG2VR/ToonOutline";
        private const string AdditiveRedrawName = "BG2VR/AdditiveRedraw";

        private static bool s_loaded;
        private static readonly Dictionary<string, Shader> s_byName = new Dictionary<string, Shader>();

        /// <summary>VR コントローラ render model 用 unlit-opaque-textured shader。失敗時 null（呼び出し側 fallback）。</summary>
        public static Shader ControllerUnlit => Get(ControllerUnlitName);

        /// <summary>加算 UI エフェクト補正用 輝度キー alpha shader（rgb 維持・alpha=輝度×color.a）。失敗時 null。</summary>
        public static Shader UiAdditiveKeyed => Get(UiAdditiveKeyedName);

        /// <summary>選択的深度プリパス用 深度のみ書き込み shader（ColorMask 0・ZWrite On）。失敗時 null（コントローラ遮蔽 OFF）。</summary>
        public static Shader DepthOnly => Get(DepthOnlyName);

        /// <summary>手モデル専用 URP 非依存 Toon shader（2-tone shade / rim / matcap・global uniform _BG2VR_HandLightDir/Color 読み）。失敗時 null（呼び出し側 fallback）。</summary>
        public static Shader HandToonOverlay => Get(HandToonOverlayName);

        /// <summary>inverted-hull アウトライン shader（裏面を法線方向に膨張・solid 色・別レンダラーとして描画）。失敗時 null（アウトライン無し）。</summary>
        public static Shader ToonOutline => Get(ToonOutlineName);

        /// <summary>加算透過材質の後段 redraw 用 URP 非依存 unlit additive shader。</summary>
        public static Shader AdditiveRedraw => Get(AdditiveRedrawName);

        private static Shader Get(string name)
        {
            EnsureLoaded();
            return s_byName.TryGetValue(name, out var sh) ? sh : null;
        }

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
                AssetBundle bundle = AssetBundle.LoadFromMemory(data);
                if (bundle == null)
                {
                    Plugin.Log.LogWarning("[BundledShaders] shader bundle のロードに失敗。");
                    return;
                }
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                if (shaders != null)
                {
                    foreach (var sh in shaders)
                    {
                        if (sh == null || !sh.isSupported) continue;
                        if (s_byName.ContainsKey(sh.name))
                        {
                            Plugin.Log.LogWarning($"[BundledShaders] 同名 shader が bundle に複数: '{sh.name}' (2 つ目以降は無視)");
                            continue;
                        }
                        s_byName[sh.name] = sh;
                    }
                }
                if (s_byName.Count == 0)
                    Plugin.Log.LogWarning("[BundledShaders] bundle に利用可能な Shader が無い（非サポート/0 件）。");
                else
                    Plugin.Log.LogInfo($"[BundledShaders] {s_byName.Count} shader ロード: {string.Join(", ", new List<string>(s_byName.Keys).ToArray())}");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[BundledShaders] shader bundle ロード例外: {e.Message}");
            }
        }
    }
}
