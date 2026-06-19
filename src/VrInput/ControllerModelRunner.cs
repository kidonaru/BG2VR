using System.Collections.Generic;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrInput
{
    /// <summary>
    /// VR 手元のモデル（同梱 bundle）を両手に表示する（VR/WorldUI アクティブ中）。
    /// 2 種別を切替: コントローラ公式モデル（Meta Touch Plus・左右別 FBX）/ アニメ調の手モデル
    /// （片手 FBX を mirror して対の手を生成・CC-BY 4.0）。per-hand 種別（HandModelSelector）で選択。
    /// OpenVR runtime 取得は backend 削除で不可になったため bundle 同梱へ移行（spec 2026-06-10 Phase3）。
    /// 実 FBX は複数メッシュ + リグ階層なので、モデルルート GameObject を Instantiate し、
    /// 全 renderer（MeshRenderer/SkinnedMeshRenderer・複数サブメッシュ含む）を bundle shader + texture に
    /// 再マテリアルする（import 時の lit 自動マテリアルは VR eye で真っ黒のため必ず差し替える）。
    /// pose は snapshot の RigLocal に整合オフセット（Config・F10 live）を乗せてモデルルートに適用
    /// （レーザーピッチは適用しない＝実機と 1:1）。snapshot は ProjectorRunner 単一読取点から受領。
    /// bundle のアセットは BundledControllerModels が所有（破棄しない）。Material は手単位で 1 度構築しキャッシュ。
    /// </summary>
    internal sealed class ControllerModelRunner
    {
        private sealed class HandState
        {
            // 代表マテリアル（null 判定の不変条件用）。単一tex種別=唯一/タンバリン=最初に作った色のマテリアル。
            // bundle アセットそのものではなく new Material 複製を指す（破棄してよい＝OwnedMats に登録）。
            public Material Mat;
            // 破棄対象の全マテリアル（単一tex種別は 1 枚 / タンバリンは submesh 色ごとに複数）。DestroyState で全破棄。
            public readonly List<Material> OwnedMats = new List<Material>();
            // 色駆動プロップ（タンバリン/サイリウム）専用: マテリアル色 → unlit マテリアルのキャッシュ（teardown 越しに再利用＝leak/再構築回避）。値は OwnedMats にも入る。
            public readonly Dictionary<Color, Material> PartColorMats = new Dictionary<Color, Material>();
            // 各 material の base `_Color`（明るさ倍率を乗じる前の真値）。明るさ適用の記録源＝毎フレ base×b を書く（冪等）。
            // 寿命は OwnedMats/PartColorMats と同じ（種別変更/teardown の DestroyState で Clear＝同種別 teardown は通らない）。
            public readonly Dictionary<Material, Color> MatBaseColor = new Dictionary<Material, Color>();
            public GameObject Go;       // Instantiate 済みモデルルート
            public HandModelKind? BuiltKind; // Go を構築した種別（null=未構築）。種別変更時に作り直す
            public bool WarnedNoModel;
            public HandFingerPoser Poser; // 手のみ使用（GO 構築時に生成・指ボーンをキャッシュ）
        }

        private readonly HandState m_left = new HandState();
        private readonly HandState m_right = new HandState();
        private static Shader s_fallbackShader;
        private static bool s_fallbackResolved;
        // 明るさ倍率を書き込む material プロパティ（ControllerUnlit / fallback UI/Default とも _Color を持つ）。
        private static readonly int s_colorId = Shader.PropertyToID("_Color");
        // 未 bake フォールバック警告フラグは両手共有（per-hand 隔離しない）＝未 bake は両手共通事象のため意図的。
        private bool m_warnedHandFallback;
        private bool m_warnedCameraFallback;
        private bool m_warnedTambFallback;
        private bool m_warnedGlowFallback;

        // Meta Touch Plus FBX のネイティブユニット→メートル換算（構造定数）。
        // 実測（BG2DevBridge 2026-06-10）: モデルのネイティブ bbox ≈ 12.5 ユニット（実物 ~0.18m に対し約70倍）。
        // = FBX のユニットが過大なため一律補正する。実機チューニングの effective scale ≈0.0104 を round 化し、
        // CtrlModelScale=1.0（中立既定）で実寸になるよう 0.01 を採る。サイズの微調整は CtrlModelScale で行う。
        private const float ModelBaseScale = 0.01f;
        // 手モデル FBX のネイティブユニット→メートル換算（構造定数・リグ依存）。
        // Player hand「Hand for VR」は globalScale=1 import で実寸が小さく、1.0 が手元実寸に合致（実機確定 2026-06-11）。
        // 微調整は HandModelScale で行う（effective = HandModelScale[0.1-3.0] × 1.0）。
        private const float HandModelBaseScale = 1.0f;
        // Cheki 用 iPhone X Lowpoly FBX のネイティブユニット→メートル換算（構造定数）。
        // 実測 native bounds(bake 診断 2026-06-17) = (1.83, 3.65, 0.24) ユニット → 最大辺(高さ) 3.65 ×0.04 ≈ 0.146m =
        // iPhone 実寸(高さ ~14cm)。微調整は CameraModelScale で行う（effective = CameraModelScale[0.1-3.0] × 0.04）。
        private const float CameraModelBaseScale = 0.04f;
        // カラオケ左手タンバリン OBJ のネイティブユニット→メートル換算（構造定数）。
        // 実測（OBJ 頂点 bbox 2026-06-13）= 直径 2.18 ユニット → ×0.1 ≈ 0.22m = 手持ちタンバリン実寸。
        // 微調整は KaraokeTambScale で行う（effective = KaraokeTambScale[0.1-3.0] × 0.1）。
        private const float TambourineBaseScale = 0.1f;
        // カラオケ右手サイリウム FBX（自作 Saliyum_Pink）のネイティブユニット→メートル換算（構造定数）。
        // 実測（bake 診断 2026-06-14）native bounds=(0.34, 2.95, 0.34)＝長辺 2.95 ユニット → ×0.068 ≈ 0.2m = 手持ちサイリウム実寸。
        // 微調整は KaraokeGlowScale で行う（effective = KaraokeGlowScale[0.1-3.0] × 0.068）。
        private const float GlowStickBaseScale = 0.068f;

        // テクスチャ非同梱時の手のフラット肌色（アニメ調のフォールバック）。ゲームはトゥーン絵柄なので暖色寄りの肌。
        private static readonly Color HandSkinColor = new Color(1.0f, 0.85f, 0.74f, 1f);

        public void Tick(Transform rig, in VrControllerSnapshot left, in VrControllerSnapshot right,
            HandModelKind leftKind, HandModelKind rightKind)
        {
            if (!Configs.ShowControllerModel.Value) { HideAll(); return; }

            // 種別は手ごとに解決（per-hand 切替＝grip+トリガーで ProjectorRunner が selector を回す）。
            // 例外は hand 単位で隔離する（片手の壊れたモデルが両手を巻き込まない）。
            TickOneHand(m_left, rig, left, VrHand.Left, leftKind);
            TickOneHand(m_right, rig, right, VrHand.Right, rightKind);
        }

        // 1 手分: 種別フォールバック → offset/scale 解決 → TickHandSafe。
        private void TickOneHand(HandState h, Transform rig, in VrControllerSnapshot snap, VrHand hand, HandModelKind requested)
        {
            HandModelKind kind = requested;
            // 手指定だが手モデル未 bake のときはコントローラにフォールバック（手元が空にならない）。
            if (kind == HandModelKind.Hand && BundledControllerModels.GetHandPrefab() == null)
            {
                if (!m_warnedHandFallback)
                {
                    m_warnedHandFallback = true;
                    Plugin.Log.LogWarning("[ControllerModel] 手モデルが bundle に無い（未 bake?）。コントローラモデルにフォールバック。");
                }
                kind = HandModelKind.Controller;
            }
            // Cheki カメラ指定だがカメラモデル未 bake のときもコントローラにフォールバック（手元が空にならない）。
            if (kind == HandModelKind.Camera && BundledControllerModels.GetCameraPrefab() == null)
            {
                if (!m_warnedCameraFallback)
                {
                    m_warnedCameraFallback = true;
                    Plugin.Log.LogWarning("[ControllerModel] カメラモデルが bundle に無い（未 bake?）。コントローラモデルにフォールバック。");
                }
                kind = HandModelKind.Controller;
            }
            // カラオケ タンバリン/サイリウム指定だが未 bake のときもコントローラにフォールバック。
            if (kind == HandModelKind.Tambourine && BundledControllerModels.GetTambourinePrefab() == null)
            {
                if (!m_warnedTambFallback)
                {
                    m_warnedTambFallback = true;
                    Plugin.Log.LogWarning("[ControllerModel] タンバリンモデルが bundle に無い（未 bake?）。コントローラモデルにフォールバック。");
                }
                kind = HandModelKind.Controller;
            }
            if (kind == HandModelKind.GlowStick && BundledControllerModels.GetGlowStickPrefab() == null)
            {
                if (!m_warnedGlowFallback)
                {
                    m_warnedGlowFallback = true;
                    Plugin.Log.LogWarning("[ControllerModel] サイリウムモデルが bundle に無い（未 bake?）。コントローラモデルにフォールバック。");
                }
                kind = HandModelKind.Controller;
            }

            // 解決済みオフセットを毎フレ read（F10 live 反映が自動成立・Subscribe 不要）。
            Vector3 posOffset;
            Quaternion rotOffset; // euler→Quaternion は実 Unity で（純関数は Quaternion を受ける＝テスト host で ECall 不可）。
            float scale;
            bool isLeftAsset; // 手のみ使用: 同梱 FBX が左手か（反対の手を mirror で生成）
            if (kind == HandModelKind.Hand)
            {
                posOffset = new Vector3(
                    Configs.HandModelPosOffsetX.Value, Configs.HandModelPosOffsetY.Value, Configs.HandModelPosOffsetZ.Value);
                rotOffset = Quaternion.Euler(
                    Configs.HandModelRotOffsetX.Value, Configs.HandModelRotOffsetY.Value, Configs.HandModelRotOffsetZ.Value);
                scale = Configs.HandModelScale.Value;
                isLeftAsset = Configs.HandModelIsLeft.Value;
            }
            else if (kind == HandModelKind.Camera)
            {
                posOffset = new Vector3(
                    Configs.CameraModelPosOffsetX.Value, Configs.CameraModelPosOffsetY.Value, Configs.CameraModelPosOffsetZ.Value);
                rotOffset = Quaternion.Euler(
                    Configs.CameraModelRotOffsetX.Value, Configs.CameraModelRotOffsetY.Value, Configs.CameraModelRotOffsetZ.Value);
                scale = Configs.CameraModelScale.Value;
                isLeftAsset = false; // カメラは mirror なし
            }
            else if (kind == HandModelKind.Tambourine)
            {
                posOffset = new Vector3(
                    Configs.KaraokeTambPosOffsetX.Value, Configs.KaraokeTambPosOffsetY.Value, Configs.KaraokeTambPosOffsetZ.Value);
                rotOffset = Quaternion.Euler(
                    Configs.KaraokeTambRotOffsetX.Value, Configs.KaraokeTambRotOffsetY.Value, Configs.KaraokeTambRotOffsetZ.Value);
                scale = Configs.KaraokeTambScale.Value;
                isLeftAsset = false; // プロップは mirror なし
            }
            else if (kind == HandModelKind.GlowStick)
            {
                posOffset = new Vector3(
                    Configs.KaraokeGlowPosOffsetX.Value, Configs.KaraokeGlowPosOffsetY.Value, Configs.KaraokeGlowPosOffsetZ.Value);
                rotOffset = Quaternion.Euler(
                    Configs.KaraokeGlowRotOffsetX.Value, Configs.KaraokeGlowRotOffsetY.Value, Configs.KaraokeGlowRotOffsetZ.Value);
                scale = Configs.KaraokeGlowScale.Value;
                isLeftAsset = false; // プロップは mirror なし
            }
            else
            {
                posOffset = new Vector3(
                    Configs.CtrlModelPosOffsetX.Value, Configs.CtrlModelPosOffsetY.Value, Configs.CtrlModelPosOffsetZ.Value);
                rotOffset = Quaternion.Euler(
                    Configs.CtrlModelRotOffsetX.Value, Configs.CtrlModelRotOffsetY.Value, Configs.CtrlModelRotOffsetZ.Value);
                scale = Configs.CtrlModelScale.Value;
                isLeftAsset = false; // コントローラは未使用
            }

            TickHandSafe(h, rig, snap, hand, kind, posOffset, rotOffset, scale, isLeftAsset);
        }

        private void TickHandSafe(HandState h, Transform rig, in VrControllerSnapshot snap, VrHand hand,
            HandModelKind kind, Vector3 posOffset, Quaternion rotOffset, float scale, bool isLeftAsset)
        {
            try { TickHand(h, rig, snap, hand, kind, posOffset, rotOffset, scale, isLeftAsset); }
            catch (System.Exception e)
            {
                if (!h.WarnedNoModel)
                {
                    h.WarnedNoModel = true;
                    Plugin.Log.LogWarning($"[ControllerModel] {hand} 想定外エラーのため非表示: {e}");
                }
                if (h.Go != null) h.Go.SetActive(false);
            }
        }

        /// <summary>VR 非 active / WorldUi 無効時（ProjectorRunner の早期 return 経路）の残像防止。</summary>
        public void HideAll()
        {
            if (m_left.Go != null) m_left.Go.SetActive(false);
            if (m_right.Go != null) m_right.Go.SetActive(false);
        }

        private void TickHand(HandState h, Transform rig, in VrControllerSnapshot snap, VrHand hand,
            HandModelKind kind, Vector3 posOffset, Quaternion rotOffset, float scale, bool isLeftAsset)
        {
            // 種別が変わったら GO/Mat とも作り直す（前種別の Cull/tex/メッシュを引き継がせない）。
            // 判定は BuiltKind で行う＝Go!=null ガードにしない: teardown で Go が fake-null の間に kind が
            // 変わる（Cheki 突入で Controller→Camera）と Go!=null ガードでは作り直しを取りこぼし、persist した
            // 旧種別 Mat（＝テクスチャ）が新メッシュに乗る（カメラにコントローラ tex が乗る実害・2026-06-13
            // BG2DevBridge で bundle 側 tex=正・実描画 Mat=旧 と確認）。fake-null への Destroy は no-op で安全。
            if (h.BuiltKind != null && h.BuiltKind != kind) DestroyState(h);

            // GO 生成 / rig teardown 道連れ破棄（fake-null）後の再生成（同種別なら Material 保持＝再構築不要。
            // 種別変更時は上の DestroyState で Mat も破棄済み＝下で作り直す）。
            if (h.Go == null)
            {
                // 手は片手 FBX を両手で共有（mirror で対を作る）。カメラ/タンバリン/サイリウムは単体（mirror なし）。
                // コントローラは左右別 FBX。
                GameObject prefab =
                      kind == HandModelKind.Camera ? BundledControllerModels.GetCameraPrefab()
                    : kind == HandModelKind.Tambourine ? BundledControllerModels.GetTambourinePrefab()
                    : kind == HandModelKind.GlowStick ? BundledControllerModels.GetGlowStickPrefab()
                    : kind == HandModelKind.Hand ? BundledControllerModels.GetHandPrefab()
                    : BundledControllerModels.GetModelPrefab(hand);
                if (prefab == null)
                {
                    // bundle 未 bake / アセット不在 → 非表示（loader がキャッシュ済＝再呼びは安価）。
                    if (!h.WarnedNoModel)
                    {
                        h.WarnedNoModel = true;
                        Plugin.Log.LogWarning($"[ControllerModel] {kind}/{hand} モデルが bundle に無い（未 bake?）。非表示。");
                    }
                    return;
                }

                h.Go = Object.Instantiate(prefab);
                h.Go.name = $"BG2VR_{ModelTag(kind)}_{hand}";
                h.Go.hideFlags = HideFlags.HideAndDontSave;
                SetLayerRecursive(h.Go.transform, VrLayers.VisualsPostProcessed); // post 反映層（main pass 残留・グレーディング/Bloom が乗る・UiSceneVoid 中も eye 可視）
                // import の Animator/rig が混じっても静的表示で困らないよう Animator は止める。
                foreach (var an in h.Go.GetComponentsInChildren<Animator>(true)) an.enabled = false;

                if (kind == HandModelKind.Tambourine || kind == HandModelKind.GlowStick || kind == HandModelKind.Camera)
                {
                    // 色駆動プロップ（タンバリン/サイリウム/iPhone カメラ）はテクスチャ無し＝submesh ごとのマテリアル色を unlit へコピーして割当てる（単一マテリアル化で色潰れを防ぐ）。
                    AssignColorDrivenMaterials(h);
                }
                else
                {
                    // 単一テクスチャ種別（コントローラ/手）: 1 枚を構築し全 renderer/submesh に上書き。
                    if (h.Mat == null)
                    {
                        h.Mat = BuildMaterial(hand, kind, out Color baseColor);
                        if (h.Mat != null)
                        {
                            h.OwnedMats.Add(h.Mat); // 破棄対象に登録（重複登録は h.Mat!=null ガードで防止）
                            h.MatBaseColor[h.Mat] = baseColor; // 明るさ適用の base 記録源
                        }
                    }
                    // 全 renderer（本体 + 電池 quad 等・複数サブメッシュ含む）を bundle マテリアルへ差し替え。
                    foreach (var r in h.Go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (h.Mat != null)
                        {
                            var mats = r.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) mats[i] = h.Mat;
                            r.sharedMaterials = mats;
                        }
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = false;
                    }
                }
                h.BuiltKind = kind;
                // 手は指ベンド用に指ボーン+rest をキャッシュ（コントローラは不要）。Animator 無効化の後に呼ぶ。
                if (kind == HandModelKind.Hand)
                {
                    if (h.Poser == null) h.Poser = new HandFingerPoser();
                    h.Poser.Build(h.Go);
                }
            }
            if (h.Go.transform.parent != rig) h.Go.transform.SetParent(rig, false); // rig 差し替え追従

            // 手は同梱 FBX（左手）と反対側を negative X scale で mirror。mirror 側は pose オフセットも X 平面で
            // 鏡映し、単一の offset config で左右が対称に整うようにする（pos は X 反転 / rot は MirrorRotationX）。
            // これをしないと mirror 側だけ回転が逆を向く（実機で左右の向きが食い違う）。
            bool mirror = kind == HandModelKind.Hand && (hand == VrHand.Left) != isLeftAsset;
            Vector3 effPosOffset = mirror ? new Vector3(-posOffset.x, posOffset.y, posOffset.z) : posOffset;
            Quaternion effRotOffset = mirror ? ControllerModelPose.MirrorRotationX(rotOffset) : rotOffset;

            ControllerModelPose.Compute(snap.RigLocalPosition, snap.RigLocalRotation, effPosOffset, effRotOffset,
                out Vector3 localPos, out Quaternion localRot);
            h.Go.transform.localPosition = localPos;
            h.Go.transform.localRotation = localRot;
            // scale と mirror 符号を純関数で一括算出（scale 代入後に mirror を上書きする順序事故を防ぐ）。
            h.Go.transform.localScale =
                  kind == HandModelKind.Hand ? ControllerModelPose.HandModelScaleVector(scale, HandModelBaseScale, mirror)
                : kind == HandModelKind.Camera ? Vector3.one * (scale * CameraModelBaseScale) // カメラ FBX(iPhone) も構造定数でユニット過大を補正
                : kind == HandModelKind.Tambourine ? Vector3.one * (scale * TambourineBaseScale)
                : kind == HandModelKind.GlowStick ? Vector3.one * (scale * GlowStickBaseScale)
                : Vector3.one * (scale * ModelBaseScale); // コントローラは構造定数で素のユニット過大を補正

            // 指ベンド: 人差し指=trigger / 親中薬小=grip。自前GOのため復元不要（rest から毎フレ再計算）。
            // mirror（右手）は上方で算出済みの同名 var を渡す（curl 符号の左右対称に使う）。
            if (kind == HandModelKind.Hand && h.Poser != null)
            {
                if (Configs.BendFingers.Value)
                {
                    var fp = new FingerCurlParams
                    {
                        InitialDeg = Configs.FingerInitialCurlDeg.Value,
                        ThumbInitialDeg = Configs.ThumbInitialCurlDeg.Value,
                        MaxDeg = Configs.FingerCurlMaxDeg.Value,
                        ThumbMaxDeg = Configs.ThumbCurlMaxDeg.Value,
                        Tau = Configs.FingerCurlSmoothTau.Value,
                    };
                    h.Poser.Pose(snap, mirror, Time.deltaTime, fp);
                }
                else
                    h.Poser.Relax(); // OFF にした瞬間に rest へ戻す
            }

            // 明るさ倍率を毎フレ反映（F10 live・Subscribe 不要＝既存 offset config と同方式）。
            // base×b を書く＝冪等。既定 1.0 は base そのまま＝現状と同値（回帰ゼロ）。
            ApplyBrightness(h, Configs.CtrlModelBrightness.Value);

            // Mat 無し（shader 全候補 strip）は描画するとマゼンタになるため非表示にする。
            h.Go.SetActive(snap.Valid && h.Mat != null);
        }

        /// <summary>手元モデル material の明るさを反映する。記録源 MatBaseColor を直接走査し、各 material の
        /// base `_Color` に倍率を乗じて書く（OwnedMats 走査＋lookup だと記録漏れ material が黙ってスキップ
        /// される＝適用漏れになるため、記録源を直接回して「記録した material は必ず適用」を構造保証する）。
        /// bundled/UI-Default fallback とも `_Color` を持つので機能する（存在しなくても SetColor は無害 no-op）。</summary>
        private static void ApplyBrightness(HandState h, float brightness)
        {
            foreach (var kv in h.MatBaseColor)
                if (kv.Key != null)
                    kv.Key.SetColor(s_colorId, ControllerModelPose.Brightened(kv.Value, brightness));
        }

        /// <summary>種別変更時に GO とマテリアルを破棄してリセット（前種別の Cull/tex を引き継がせない）。</summary>
        private static void DestroyState(HandState h)
        {
            if (h.Go != null) Object.Destroy(h.Go);
            // 所有マテリアルを全破棄（単一tex種別=1枚 / タンバリン=submesh 色ごとに複数）。
            // new Material で生成した複製＝破棄してよい（bundle 資産ではない）。h.Mat は代表＝OwnedMats に含まれる。
            foreach (var m in h.OwnedMats) if (m != null) Object.Destroy(m);
            h.OwnedMats.Clear();
            h.PartColorMats.Clear();
            h.MatBaseColor.Clear(); // base 記録源も同寿命で破棄（OwnedMats と心中＝stale 防止）
            h.Go = null;
            h.Mat = null;
            h.BuiltKind = null;
            h.WarnedNoModel = false;
            h.Poser = null; // 種別変更/teardown 時は指ボーンキャッシュも破棄（次回 Build で作り直す）
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        // GO 名表示用の種別タグ（デバッグ視認のみ）。
        private static string ModelTag(HandModelKind kind) =>
            kind == HandModelKind.Hand ? "HandModel"
            : kind == HandModelKind.Camera ? "CameraModel"
            : kind == HandModelKind.Tambourine ? "Tambourine"
            : kind == HandModelKind.GlowStick ? "GlowStick"
            : "CtrlModel";

        private static Material BuildMaterial(VrHand hand, HandModelKind kind, out Color baseColor)
        {
            Texture2D tex = kind == HandModelKind.Hand
                ? BundledControllerModels.GetHandTexture()
                : BundledControllerModels.GetTexture(hand);
            // base `_Color`（明るさ倍率の乗算元）: tex モデルは白（tex を素通し）/ tex 無しは単色（下の mat.color と同値）。
            baseColor = tex != null ? Color.white
                : kind == HandModelKind.Hand ? HandSkinColor : new Color(0.5f, 0.5f, 0.5f, 1f);
            // 同梱 unlit-opaque-textured shader を優先。bundle 失敗時のみ既製 UI/Default に degrade
            //（fallback は ZWrite Off で solid メッシュの深度ソートが崩れる＝最終手段・bundle shader が正規。
            //  実運用では bg2vr_shaders も同梱前提のため通常は発火しない・plan-review 🟡-2）。
            Shader bundled = BundledShaders.ControllerUnlit;
            Shader shader = bundled != null ? bundled : FindFallbackShader();
            Material mat = shader != null ? new Material(shader) : null;
            if (mat != null)
            {
                mat.hideFlags = HideFlags.HideAndDontSave;
                if (tex != null) mat.mainTexture = tex;
                else mat.color = baseColor; // tex 無し → 単色（baseColor と同値・初期値。以後は明るさ適用が _Color を上書き）
                // 手元ビジュアルはレーザーと同じ frontmost queue（UI パネルより手前）。
                if (bundled != null)
                {
                    // ZTest LEqual（自己オクルージョン正常）+ queue 4002 で UI より手前（UI は ZWrite Off）。
                    mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                    // 手は片手を mirror（negative X scale）して対を作るため winding が反転して裏面化する
                    //  → 両面描画(Cull Off)で解消（unlit フラットなので破綻なし）。コントローラは既定 Back のまま。
                    // （iPhone カメラは色駆動経路 AssignColorDrivenMaterials で構築＝この BuildMaterial には来ない。
                    //  色駆動側は閉じたソリッド・mirror なしで既定 Back のまま＝winding 正。面欠けは実機観測後に対処。）
                    if (kind == HandModelKind.Hand)
                        mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    mat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;
                }
                else
                {
                    BG2VR.WorldUi.UiOverlayRenderPolicy.Apply(mat,
                        BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue, false);
                }
            }
            Plugin.Log.LogInfo($"[ControllerModel] {kind}/{hand} マテリアル構築"
                + $"（tex={(tex != null ? $"{tex.width}x{tex.height}" : "なし→単色")}, shader={(bundled != null ? "bundled" : "fallback")}）");
            return mat;
        }

        /// <summary>色駆動プロップ（テクスチャ無し・マテリアル色で描く）の materials を submesh ごとに構築・割当てる。
        /// 対象 = タンバリン（OBJ・4 Kd 色）/ サイリウム（FBX Saliyum_Pink・5 パーツの Phong 色）。
        /// 各 submesh の元マテリアル `.color`（OBJ import の Kd / FBX import の DiffuseColor が `_Color`/`_BaseColor` に入る）を
        /// unlit へコピーし、色ごとに 1 枚を dedupe して作る（全 submesh に単一マテリアルを上書きすると色が潰れるため）。
        /// 生成物は h.PartColorMats（色キャッシュ・teardown 越し再利用）と h.OwnedMats（破棄対象）に登録する。</summary>
        private static void AssignColorDrivenMaterials(HandState h)
        {
            Shader bundled = BundledShaders.ControllerUnlit;
            Shader shader = bundled != null ? bundled : FindFallbackShader();
            int built = 0;
            foreach (var r in h.Go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Color c = src[i] != null ? ReadSourceColor(src[i]) : new Color(0.5f, 0.5f, 0.5f, 1f);
                    if (!h.PartColorMats.TryGetValue(c, out Material m))
                    {
                        m = CreateUnlitTinted(shader, bundled != null, c);
                        h.PartColorMats[c] = m; // null も含めキャッシュ（shader 不在は session 中不変）
                        if (m != null)
                        {
                            h.OwnedMats.Add(m);
                            h.MatBaseColor[m] = c; // 明るさ適用の base 記録源（submesh の元色）
                            if (h.Mat == null) h.Mat = m; // 代表（null 判定の不変条件用＝SetActive/再構築ガード）
                            built++;
                        }
                    }
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // shader 全候補 strip（bundled も fallback も null）＝代表 Mat が立たず非表示になる。原因切り分け用に警告。
            if (h.Mat == null)
                Plugin.Log.LogWarning("[ControllerModel] 色駆動プロップのマテリアル構築失敗（shader 不在＝bundled/fallback とも null）。非表示。");
            else
                Plugin.Log.LogInfo($"[ControllerModel] 色駆動プロップのマテリアル構築（新規色数={built}, shader={(bundled != null ? "bundled" : "fallback")}）");
        }

        /// <summary>元マテリアルの基本色を読む。OBJ import の Kd / FBX import の DiffuseColor が Standard `_Color` に入る前提。
        /// URP 等で `_BaseColor` の場合に備えフォールバック。どちらも無ければ既定灰。</summary>
        private static Color ReadSourceColor(Material m)
        {
            if (m.HasProperty("_Color")) return m.color;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return new Color(0.5f, 0.5f, 0.5f, 1f);
        }

        /// <summary>テクスチャ無し・単色 tint の unlit マテリアルを 1 枚作る（色駆動プロップ submesh 用）。
        /// 既存 BuildMaterial と同じ前面ポリシー（ZTest LEqual + LaserQueue）。色駆動プロップ（タンバリン/サイリウム）は
        /// 閉じたソリッド（mirror しない）ので Cull は既定 Back のまま＝winding 正。shader 不在なら null。
        /// 実機で裏面の面欠けが出たら Cull Off へ（未観測なので保留＝予防コード回避）。</summary>
        private static Material CreateUnlitTinted(Shader shader, bool bundled, Color color)
        {
            if (shader == null) return null;
            Material mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.color = color; // mainTexture なし＝_Color の単色
            if (bundled)
            {
                mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                mat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;
            }
            else
            {
                BG2VR.WorldUi.UiOverlayRenderPolicy.Apply(mat, BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue, false);
            }
            return mat;
        }

        private static Shader FindFallbackShader()
        {
            if (s_fallbackResolved) return s_fallbackShader;
            s_fallbackResolved = true;
            // lit 系（Standard/Legacy Diffuse）は layer30 で暗く描画＝不可。unlit 既製を使う。
            string[] candidates = { "UI/Default", "Sprites/Default" };
            foreach (string name in candidates)
            {
                Shader sh = Shader.Find(name);
                if (sh != null)
                {
                    Plugin.Log.LogInfo($"[ControllerModel] fallback shader 採用: {name}");
                    return s_fallbackShader = sh;
                }
            }
            Plugin.Log.LogWarning("[ControllerModel] 利用可能 shader なし（モデルはマゼンタ表示になり得る）");
            return null;
        }
    }
}
