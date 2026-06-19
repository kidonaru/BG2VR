using System.IO;
using System.Reflection;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrInput
{
    /// <summary>
    /// Meta 公式 Touch Plus モデル（左右の GameObject ルート + BaseColor_AO Texture）を同梱 AssetBundle から
    /// ロードする（一度だけ・キャッシュ）。OpenXR には runtime render model API が無いため bundle 同梱
    /// （Oculus PCVR は XR_FB_render_model 非対応・2026-06-10 調査）。bundle 名 = BG2VR.Resources.bg2vr_models。
    /// 未 bake（リソース不在）は全 null で degrade（手元非表示）。
    /// 実 FBX は複数メッシュ + リグ階層なので Mesh 単位でなく **GameObject ルートをアセット名で引く**。
    /// アセット名（= FBX/PNG のファイル名）は決定的。LoadAsset で取れない時のみ名前 substring で fallback。
    /// </summary>
    internal static class BundledControllerModels
    {
        private const string ResourceSuffix = "bg2vr_models";
        // bundle 内アセット名（= 配置ファイル名・拡張子なし）。LoadAsset は case-insensitive。
        private const string LeftModelName = "MetaQuestTouchPlus_Left";
        private const string RightModelName = "MetaQuestTouchPlus_Right";
        private const string LeftTexName = "MetaQuestTouchPlus_Left_BaseColor_AO";
        private const string RightTexName = "MetaQuestTouchPlus_right_BaseColor_AO"; // right は小文字（v1.8 実ファイル名）
        // VR 用手モデル（片手 FBX・CC-BY 4.0）。アセット名 = FBX ファイル名から拡張子を除いたもの。
        private const string HandModelName = "Player hand";
        // Cheki 用 iPhone X Lowpoly モデル（FBX・CC-BY 4.0）。FBX root GO 名 = ファイル名（拡張子なし・空白込み）。
        // 取れねば substr "iphone" で fallback。テクスチャ非同梱の色駆動モデル＝各 submesh の Phong 色を
        // runtime で unlit へ転写するため bundle 内テクスチャは持たない（タンバリン/サイリウムと同型）。
        private const string CameraModelName = "iphone model";
        // カラオケ左手プロップ: タンバリン（OBJ・テクスチャ無し＝submesh の Kd 色で描画するためテクスチャ非同梱）。
        // OBJ import の root GO 名 = ファイル名（拡張子なし）。取れねば substr "tamb" で fallback。
        private const string TambourineModelName = "tamberine";
        // カラオケ右手プロップ: サイリウム（自作 FBX Saliyum_Pink・テクスチャ無し＝5 パーツの Phong マテリアル色で描画）。
        // タンバリンと同じ色駆動プロップ＝bake はマテリアル取込を残し、ControllerModelRunner が各パーツ色を unlit へ転写する。
        private const string GlowStickModelName = "Saliyum_Pink";

        private static bool s_loaded;
        private static GameObject s_leftModel, s_rightModel;
        private static Texture2D s_leftTex, s_rightTex;
        private static GameObject s_handModel;
        private static Texture2D s_handTex;
        private static GameObject s_cameraModel;
        private static GameObject s_tambourineModel;
        private static GameObject s_glowStickModel;

        /// <summary>左右のモデルルート GameObject（prefab 相当）。呼び出し側で Instantiate する。null=未 bake/不在。</summary>
        public static GameObject GetModelPrefab(VrHand hand) { EnsureLoaded(); return hand == VrHand.Left ? s_leftModel : s_rightModel; }
        public static Texture2D GetTexture(VrHand hand) { EnsureLoaded(); return hand == VrHand.Left ? s_leftTex : s_rightTex; }

        /// <summary>手モデルルート GameObject（片手・呼び出し側で Instantiate し対の手は mirror）。null=未 bake/不在。</summary>
        public static GameObject GetHandPrefab() { EnsureLoaded(); return s_handModel; }
        /// <summary>手モデルの albedo テクスチャ（FBX 埋め込みがあれば）。null なら Runner 側でフラット肌色にフォールバック。</summary>
        public static Texture2D GetHandTexture() { EnsureLoaded(); return s_handTex; }

        /// <summary>Cheki 用カメラモデル(iPhone)ルート GameObject（呼び出し側で Instantiate・mirror なし）。null=未 bake/不在。
        /// テクスチャは持たない（色駆動＝各 submesh の Phong 色を Runner が unlit へ転写）。</summary>
        public static GameObject GetCameraPrefab() { EnsureLoaded(); return s_cameraModel; }

        /// <summary>カラオケ左手プロップ: タンバリンモデルルート GameObject（mirror なし）。null=未 bake/不在。
        /// テクスチャは持たず submesh の Kd 色で描画する（ControllerModelRunner が material をパーツごとに構築）。</summary>
        public static GameObject GetTambourinePrefab() { EnsureLoaded(); return s_tambourineModel; }
        /// <summary>カラオケ右手プロップ: サイリウムモデルルート GameObject（mirror なし）。テクスチャを持たず
        /// 各パーツのマテリアル色で描画する（ControllerModelRunner が色駆動経路で unlit を構築）。null=未 bake/不在。</summary>
        public static GameObject GetGlowStickPrefab() { EnsureLoaded(); return s_glowStickModel; }

        private static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true; // 失敗も含めキャッシュ（bundle ロードは決定的）
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
                    Plugin.Log.LogWarning("[ControllerModel] モデル bundle が見つからない（未 bake?）。手元モデルは非表示。");
                    return;
                }
                byte[] data;
                using (var s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null) { Plugin.Log.LogWarning("[ControllerModel] モデル bundle ストリーム取得不可。"); return; }
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                }
                AssetBundle bundle = AssetBundle.LoadFromMemory(data);
                if (bundle == null) { Plugin.Log.LogWarning("[ControllerModel] モデル bundle ロード失敗。"); return; }

                s_leftModel = ResolveByName<GameObject>(bundle, LeftModelName, "left");
                s_rightModel = ResolveByName<GameObject>(bundle, RightModelName, "right");
                s_leftTex = ResolveByName<Texture2D>(bundle, LeftTexName, "left");
                s_rightTex = ResolveByName<Texture2D>(bundle, RightTexName, "right");
                s_handModel = ResolveByName<GameObject>(bundle, HandModelName, "hand");
                // Cheki カメラ(iPhone・FBX・未 bake なら null で degrade→Runner がコントローラへフォールバック)。
                // テクスチャは持たない（色駆動）＝モデルのみ解決する（各 submesh の Phong 色は Runner が描画時に読む）。
                s_cameraModel = ResolveByName<GameObject>(bundle, CameraModelName, "iphone");
                // カラオケプロップ（未 bake なら null で degrade→Runner がコントローラへフォールバック）。
                // サイリウムはテクスチャ無し（色駆動）＝モデルのみ解決する（パーツ色は Runner が描画時に読む）。
                s_tambourineModel = ResolveByName<GameObject>(bundle, TambourineModelName, "tamb");
                s_glowStickModel = ResolveByName<GameObject>(bundle, GlowStickModelName, "saliyum");
                // 手モデル tex（片手・未 bake なら null で degrade）。FBX 埋め込み名が不定なので専用解決
                // （非 albedo マップ + コントローラ tex を除外）。iPhone は色駆動でテクスチャ非同梱＝探索に混入しない。
                // 無ければ null→フラット肌色。
                s_handTex = ResolveHandTexture(bundle);

                Plugin.Log.LogInfo($"[ControllerModel] モデル bundle ロード: "
                    + $"L(model={s_leftModel != null},tex={s_leftTex != null}) / R(model={s_rightModel != null},tex={s_rightTex != null})"
                    + $" / Hand(model={s_handModel != null},tex={s_handTex != null})"
                    + $" / Camera(model={s_cameraModel != null})"
                    + $" / Tambourine(model={s_tambourineModel != null})"
                    + $" / GlowStick(model={s_glowStickModel != null})");
                // bundle 本体はアンロードしない（BundledShaders と同方針＝取り出し済みアセット保持の確実性優先）
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[ControllerModel] モデル bundle ロード例外: {e.Message}");
            }
        }

        /// <summary>手モデルの albedo テクスチャ解決。FBX 埋め込み名が不定なので、(1) 別 PNG bake 時の exact 候補名を優先、
        /// (2) 取れなければ bundle 内 Texture2D から「コントローラ tex」「カメラ tex」と「非 albedo マップ(normal/ao/rough/
        /// metal/spec/emission/height/mask)」を除外し、残りの最大解像度を albedo とみなす（補助マップより本体が大きい想定）。
        /// 汎用 substr マッチ（ResolveByName）だと normal/AO を誤って mainTexture に充てる事故が起きるため専用化（code-review MID）。
        /// excludes = 同 bundle の他プロップ albedo（大きい BaseColor＝最大ヒューリスティックが誤採用するため参照で除外する・generic）。
        /// 現状 iPhone/タンバリン/サイリウムは全て色駆動でテクスチャ非同梱＝除外対象は無い（呼出側は excludes 無しで呼ぶ）。</summary>
        private static Texture2D ResolveHandTexture(AssetBundle bundle, params Texture2D[] excludes)
        {
            foreach (var n in new[] { HandModelName + "_BaseColor", HandModelName + "_albedo", HandModelName })
            {
                Texture2D t = bundle.LoadAsset<Texture2D>(n);
                if (t != null) return t;
            }
            Texture2D best = null;
            foreach (Texture2D t in bundle.LoadAllAssets<Texture2D>())
            {
                if (t == null) continue;
                if (System.Array.IndexOf(excludes, t) >= 0) continue; // 他プロップ tex を手 albedo に誤採用しない
                string ln = t.name.ToLowerInvariant();
                if (ln.Contains("metaquesttouchplus")) continue; // コントローラ tex 除外
                if (ln.Contains("normal") || ln.EndsWith("_n") || ln.Contains("_ao") || ln.Contains("rough")
                    || ln.Contains("metal") || ln.Contains("spec") || ln.Contains("emis")
                    || ln.Contains("height") || ln.Contains("mask")) continue; // 非 albedo マップ除外
                if (best == null || (long)t.width * t.height > (long)best.width * best.height) best = t;
            }
            return best;
        }

        /// <summary>主経路 = アセット名で直接ロード（決定的）。取れなければ全件 substring で fallback。</summary>
        private static T ResolveByName<T>(AssetBundle bundle, string assetName, string substr) where T : Object
        {
            T direct = bundle.LoadAsset<T>(assetName);
            if (direct != null) return direct;
            foreach (var a in bundle.LoadAllAssets<T>())
            {
                if (a != null && a.name.ToLowerInvariant().Contains(substr)) return a;
            }
            Plugin.Log.LogWarning($"[ControllerModel] bundle に {typeof(T).Name} '{assetName}'（or *{substr}*）が無い。");
            return null;
        }
    }
}
