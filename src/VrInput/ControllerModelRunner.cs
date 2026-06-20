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
            public int LastResolverToken; // HandSkinMaterialResolver の Ready 遷移を追跡（採取完了時の自動再構築用）
            // 手のみ: 構築時に捕捉した素体（Cast）影色 `_1st_ShadeColor`。per-frame で ×HandShadeFactor して書き戻す
            // 記録源（live 反映の base）。unlit fallback（_1st_ShadeColor 不在）では Captured=false で適用しない。
            public Color HandShadeBase;
            public bool HandShadeBaseCaptured;
            // アウトライン（inverted-hull・別レンダラー）専用。OutlineMat=per-frame 書込先（色/太さ・OwnedMats で破棄）。
            // OutlineRenderers=構築時に保持した輪郭レンダラー群（per-frame の enabled 切替先＝毎フレ GetComponentsInChildren を避ける）。
            // 輪郭子 GO は h.Go 配下＝DestroyState の Destroy(h.Go) で道連れ破棄。
            public Material OutlineMat;
            public readonly List<Renderer> OutlineRenderers = new List<Renderer>();
        }

        private readonly HandState m_left = new HandState();
        private readonly HandState m_right = new HandState();
        private static Shader s_fallbackShader;
        private static bool s_fallbackResolved;
        // 明るさ倍率を書き込む material プロパティ（ControllerUnlit / fallback UI/Default とも _Color を持つ）。
        private static readonly int s_colorId = Shader.PropertyToID("_Color");
        // HDR emission プロパティ（HandToonOverlay のみ持つ）。サイリウム発光部に毎フレ書く。
        private static readonly int s_emissionId = Shader.PropertyToID("_EmissionColor");
        // toon 影色 / 影境界フェード（HandToonOverlay のみ持つ）。手・カラオケ楽器に毎フレ書く（F10 live）。
        private static readonly int s_shadeColorId = Shader.PropertyToID("_1st_ShadeColor");
        private static readonly int s_shadeFeatherId = Shader.PropertyToID("_ShadeFeather");
        // アウトライン色 / 太さ（ToonOutline shader）。輪郭マテリアルに毎フレ書く（F10 live）。ZTest/Cull は構築時1回。
        private static readonly int s_outlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int s_outlineWidthId = Shader.PropertyToID("_OutlineWidth");
        // 未 bake フォールバック警告フラグは両手共有（per-hand 隔離しない）＝未 bake は両手共通事象のため意図的。
        private bool m_warnedHandFallback;
        private bool m_warnedCameraFallback;
        private bool m_warnedTambFallback;
        private bool m_warnedGlowFallback;
        private bool m_warnedOutline; // アウトライン shader 未 bake 警告（両手共有・1 回のみ）

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

        // 手モデルのレイヤーは HandLayerResolver で決める（Hand=HandLighting(28) / 他=VisualsPostProcessed(29)）。
        // layer 28 は scene の全 light の cullingMask に含まれない＝scene 光の影響を一切受けない（VIP 残留 light で
        // 白飛びする退行を構造的に解消・2026-06-19）。BG2VR.HandLighting.HandLightingRunner が cullingMask=1<<28 only の
        // 自前 directional light を rig 子に spawn し、これだけが手を照らす。

        public void Tick(Transform rig, in VrControllerSnapshot left, in VrControllerSnapshot right,
            HandModelKind leftKind, HandModelKind rightKind)
        {
            if (!Configs.ShowControllerModel.Value) { HideAll(); return; }

            // ERISA Babydoll 素体マテリアルの非同期ロードを保証する（多重ガードあり・Loading/Ready/Failed なら no-op）。
            // handTex はベース構築時の main tex 焼きにのみ使う。BG2VR セッション中 GetHandTexture() は不変前提。
            // 手は常にトゥーン（HandToonOverlay）で描く＝無条件採取。採取失敗時のみ unlit fallback に落ちる。
            HandSkinMaterialResolver.EnsureBegin(BundledControllerModels.GetHandTexture());

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
            // 採取完了（ReadyToken 増分）を検出したら DestroyState 強制 → 同フレ内の下の `if (h.Go == null)` 経路で
            // 再構築される＝unlit→Toon の自動切替（採取完了の次フレで反映）。
            // 初回 Tick（h.LastResolverToken=0 / ReadyToken=0）は不一致でないので no-op＝採取前は副作用無し。
            // Idle/Loading 中（ReadyToken=0）も同様に no-op。
            else if (kind == HandModelKind.Hand
                     && h.Go != null && h.LastResolverToken != HandSkinMaterialResolver.ReadyToken)
            {
                DestroyState(h);
            }

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
                // Hand=HandLighting(28) 固定で BG2VR 自前 directional light のみが当たる構造。
                // 他種別（コントローラ/カメラ/タンバリン/サイリウム）は従来通り VisualsPostProcessed(29) 維持。
                SetLayerRecursive(h.Go.transform, HandLayerResolver.Resolve(kind));
                // import の Animator/rig が混じっても静的表示で困らないよう Animator は止める。
                foreach (var an in h.Go.GetComponentsInChildren<Animator>(true)) an.enabled = false;

                if (kind == HandModelKind.Tambourine || kind == HandModelKind.GlowStick || kind == HandModelKind.Camera)
                {
                    // 色駆動プロップ（タンバリン/サイリウム/iPhone カメラ）はテクスチャ無し＝submesh ごとのマテリアル色を割当てる（単一マテリアル化で色潰れを防ぐ）。
                    // カラオケ楽器（タンバリン/サイリウム）はアニメ調 toon（HandToonOverlay）で描く。Cheki カメラは従来 unlit のまま。
                    bool toon = kind == HandModelKind.Tambourine || kind == HandModelKind.GlowStick;
                    AssignColorDrivenMaterials(h, toon);
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
                            // 手は素体（Cast）影色を捕捉＝per-frame で ×HandShadeFactor して live 反映する base。
                            // unlit fallback（_1st_ShadeColor 不在）は Captured=false のまま＝影調整は適用しない。
                            if (kind == HandModelKind.Hand && h.Mat.HasProperty(s_shadeColorId))
                            {
                                h.HandShadeBase = h.Mat.GetColor(s_shadeColorId);
                                h.HandShadeBaseCaptured = true;
                            }
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
                        // 手は layer 28(overlay) で eye main render から除外＝どのカメラからも不可視。
                        // SkinnedMeshRenderer は不可視だと skinning がスキップされ bind pose のまま
                        // CommandBuffer.DrawRenderer 描画される（指ベンドが反映されない・実機検証 2026-06-19）。
                        // updateWhenOffscreen=true で可視性に依らず毎フレ skinning させる。
                        if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                    }
                }
                h.BuiltKind = kind;
                h.LastResolverToken = HandSkinMaterialResolver.ReadyToken; // 構築時の token を記録（次回 Ready 遷移で再構築）
                // 手は指ベンド用に指ボーン+rest をキャッシュ（コントローラは不要）。Animator 無効化の後に呼ぶ。
                if (kind == HandModelKind.Hand)
                {
                    if (h.Poser == null) h.Poser = new HandFingerPoser();
                    h.Poser.Build(h.Go);
                }
                // アウトライン（inverted-hull）を別レンダラーとして構築（手/カラオケ楽器のみ・非対象は no-op）。
                // 本体 renderer の再マテリアル走査は上で完了済み＝後から足す輪郭子は拾われない（double-process 回避）。
                BuildOutline(h, kind);
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

            // Hand 種別は肌色 Config（HandSkinColorR/G/B）を毎フレ 1x1 tex（HandSkinMaterialResolver の sentinel）に
            // 書き戻す＝F10 で live 反映。tex 色 × LightColor で VIP/Bar の照明色変化を自然に受ける。
            // MatBaseColor[h.Mat] = 白 で _Color が brightness 倍率の素通し（白×brightness）になる。
            // shade1/2 color は素体のまま継承（実機判断 2026-06-19）。他種別は構築時の base 値を保持。
            if (kind == HandModelKind.Hand && h.Mat != null)
            {
                var skinColor = new Color(
                    Configs.HandSkinColorR.Value,
                    Configs.HandSkinColorG.Value,
                    Configs.HandSkinColorB.Value,
                    1f);
                HandSkinMaterialResolver.UpdateSkinColor(skinColor);
                h.MatBaseColor[h.Mat] = Color.white;
                // HandToonOverlay shader の MatCap 強度を F10 live 反映。HasProperty ガードで
                // ControllerUnlit fallback には silent no-op。
                if (h.Mat.HasProperty("_MatCap_Intensity"))
                    h.Mat.SetFloat("_MatCap_Intensity", Configs.HandMatCapIntensity.Value);
                // 影の濃さ（素体影色×HandShadeFactor・既定 1.0=素体のまま）と境界フェード（HandShadeFeather・既定 0=くっきり）を
                // F10 live 反映。影色は捕捉済み base から作る＝brightness と独立。unlit fallback は Captured=false / HasProperty false で no-op。
                if (h.HandShadeBaseCaptured && h.Mat.HasProperty(s_shadeColorId))
                    h.Mat.SetColor(s_shadeColorId, ControllerModelPose.Brightened(h.HandShadeBase, Configs.HandShadeFactor.Value));
                // feather は捕捉 base 非依存（Config 直書き）＝Captured ガード不要・HasProperty のみで fallback は no-op。
                if (h.Mat.HasProperty(s_shadeFeatherId))
                    h.Mat.SetFloat(s_shadeFeatherId, Configs.HandShadeFeather.Value);
                // Cull は Resolver base で Cull=Back 固定（mirror も同一設定で正・per-hand 分岐不要・実機検証 2026-06-19）。
            }

            // 明るさ倍率を毎フレ反映（F10 live・Subscribe 不要＝既存 offset config と同方式）。
            // base×b を書く＝冪等。Hand 種別は手専用 HandModelBrightness（実機チューニング 2026-06-19 で 1.0 が既定）、
            // 他種別は従来通り CtrlModelBrightness（既定 0.8）を使う＝独立制御。
            // カラオケ楽器（タンバリン/サイリウム）は共有 global light(0.63) の暗化を補正する専用 KaraokePropBrightness。
            float brightness =
                  kind == HandModelKind.Hand ? Configs.HandModelBrightness.Value
                : (kind == HandModelKind.Tambourine || kind == HandModelKind.GlowStick) ? Configs.KaraokePropBrightness.Value
                : Configs.CtrlModelBrightness.Value;
            ApplyBrightness(h, brightness);

            // カラオケ楽器（タンバリン/サイリウム）の影の濃さ + 境界フェードを毎フレ反映（F10 live）。
            if (kind == HandModelKind.Tambourine || kind == HandModelKind.GlowStick) ApplyToonShade(h);

            // サイリウムの発光部（彩度高 submesh）に HDR emission を毎フレ書く（live・Bloom 発光）。
            // タンバリンは金/茶も彩度が高く誤発光するため scope 外（toon のみ）＝GlowStick 限定。
            if (kind == HandModelKind.GlowStick) ApplyGlow(h);

            // Mat 無し（shader 全候補 strip）は描画するとマゼンタになるため非表示にする。
            bool bodyVisible = snap.Valid && h.Mat != null;
            h.Go.SetActive(bodyVisible);

            // アウトライン（色/太さ live・enable 切替）を反映。非対象種別/未 bake は OutlineMat==null で no-op。
            ApplyOutline(h, kind, bodyVisible);
        }

        /// <summary>手元モデル material の明るさを反映する。記録源 MatBaseColor を直接走査し、各 material の
        /// base 色プロパティに倍率を乗じて書く。`_Color`（unlit / UTS legacy）と `_BaseColor`（UTS 一次・URP 系で標準）
        /// 両方に書く＝UTS Toon と自前 unlit の両方で機能する。存在しないプロパティへの Set* は無害 no-op。</summary>
        private static void ApplyBrightness(HandState h, float brightness)
        {
            foreach (var kv in h.MatBaseColor)
            {
                if (kv.Key == null) continue;
                var c = ControllerModelPose.Brightened(kv.Value, brightness);
                kv.Key.SetColor(s_colorId, c);
                if (kv.Key.HasProperty("_BaseColor")) kv.Key.SetColor("_BaseColor", c);
            }
        }

        /// <summary>カラオケ楽器（タンバリン/サイリウム）の影色 + 境界フェードを反映する（toon プロップ限定で呼ぶ）。
        /// MatBaseColor（素色）を走査し各 submesh の `_1st_ShadeColor = 素色×KaraokePropShadeFactor`・
        /// `_ShadeFeather = KaraokePropShadeFeather` を毎フレ書く（F10 live）。`_1st_ShadeColor` 不在マテリアル
        /// （unlit fallback）は no-op。影色は brightness と独立（素色から作る）。</summary>
        private static void ApplyToonShade(HandState h)
        {
            float factor = Configs.KaraokePropShadeFactor.Value;
            float feather = Configs.KaraokePropShadeFeather.Value;
            foreach (var kv in h.MatBaseColor)
            {
                if (kv.Key == null) continue;
                if (kv.Key.HasProperty(s_shadeColorId))
                    kv.Key.SetColor(s_shadeColorId, ControllerModelPose.Brightened(kv.Value, factor));
                if (kv.Key.HasProperty(s_shadeFeatherId))
                    kv.Key.SetFloat(s_shadeFeatherId, feather);
            }
        }

        /// <summary>サイリウムの発光部に HDR emission を反映する（GlowStick 限定で呼ぶ）。MatBaseColor（素色・brightness
        /// 乗算前）を走査し、彩度がしきい値以上の submesh のみ emission=素色×strength・それ以外は黒を書く（live・F10 反映）。
        /// emission は MatBaseColor（素色）から作る＝ApplyBrightness の `_Color`×PropBrightness 補正を受けない。発光強度は
        /// KaraokeGlowEmission のみで lit 明るさと独立に制御する意図。_EmissionColor 不在マテリアル（unlit fallback）は no-op。</summary>
        private static void ApplyGlow(HandState h)
        {
            float thr = Configs.KaraokeGlowSaturationThreshold.Value;
            float strength = Configs.KaraokeGlowEmission.Value;
            foreach (var kv in h.MatBaseColor)
            {
                if (kv.Key == null || !kv.Key.HasProperty(s_emissionId)) continue;
                Color emission = PropGlow.IsGlowing(kv.Value, thr)
                    ? PropGlow.EmissionColor(kv.Value, strength)
                    : Color.black;
                kv.Key.SetColor(s_emissionId, emission);
            }
        }

        /// <summary>アウトライン（inverted-hull）を別レンダラーとして構築する（手/カラオケ楽器のみ・非対象は no-op）。
        /// 元モデルの各 renderer に「同 mesh を共有し ToonOutline で描く子レンダラー」を作る。子 GO は h.Go 配下＝
        /// DestroyState で道連れ破棄。ZTest/Cull/ZWrite は構造的＝ここで 1 度だけ設定（per-frame は色/太さのみ）。
        /// SMR（手）は bones/rootBone/sharedMesh を同一参照で共有＝同じスケルトンに従動（複製しない）。bundle 不在は no-op。</summary>
        private void BuildOutline(HandState h, HandModelKind kind)
        {
            if (!(kind == HandModelKind.Hand || kind == HandModelKind.Tambourine || kind == HandModelKind.GlowStick)) return;
            Shader sh = BundledShaders.ToonOutline;
            if (sh == null)
            {
                if (!m_warnedOutline)
                {
                    m_warnedOutline = true;
                    Plugin.Log.LogWarning("[ControllerModel] アウトライン shader が bundle に無い（未 bake?）。輪郭なしで継続。");
                }
                return;
            }

            // outlineMat は per-HandState で 1 枚（左右手・各プロップで別＝色/太さ/Cull を個別設定可）。
            Material outlineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            // 手は overlay の reversed-Z で GreaterEqual（本体 toon と同じ）/ プロップは URP main で LessEqual。
            outlineMat.SetFloat("_ZTest", (float)(kind == HandModelKind.Hand
                ? UnityEngine.Rendering.CompareFunction.GreaterEqual
                : UnityEngine.Rendering.CompareFunction.LessEqual));
            // inverted-hull は裏面＝Cull Front（手は invertCulling+mirror で per-hand 出し分けが要る可能性・spec §5）。
            outlineMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            outlineMat.SetFloat("_ZWrite", 1f);
            outlineMat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;
            h.OwnedMats.Add(outlineMat);
            h.OutlineMat = outlineMat;

            // 元 renderer のみを走査（輪郭子はこの後に足すため snapshot 配列には入らない＝double-process なし）。
            foreach (var src in h.Go.GetComponentsInChildren<Renderer>(true))
            {
                if (src == null) continue;
                Mesh mesh = src is SkinnedMeshRenderer ssmrSrc ? ssmrSrc.sharedMesh
                          : src.GetComponent<MeshFilter>() is MeshFilter mfSrc ? mfSrc.sharedMesh : null;
                if (mesh == null) continue;

                var go = new GameObject("BG2VR_Outline") { hideFlags = HideFlags.HideAndDontSave };
                // 本体 renderer の子に付ける＝overlay 列挙（GetComponentsInChildren の階層順）で本体の後に来る
                // ＝描画順「本体→輪郭」が成立。reversed-Z + ZTest GreaterEqual + ZWrite で輪郭の膨張背面は本体前面
                // より遠く落ち、シルエット縁のみ残る（inverted-hull 成立）。親付けを変えると描画順が崩れる不変条件。
                go.transform.SetParent(src.transform, false); // identity local TRS＝src に座標一致で従動
                go.layer = src.gameObject.layer;               // 元と同 layer（手=28 / プロップ=29）

                Renderer outRenderer;
                if (src is SkinnedMeshRenderer ssmr)
                {
                    var dsmr = go.AddComponent<SkinnedMeshRenderer>();
                    dsmr.sharedMesh = ssmr.sharedMesh;  // mesh 共有
                    dsmr.bones = ssmr.bones;            // 同じ bone Transform 群に従動（複製しない）
                    dsmr.rootBone = ssmr.rootBone;
                    dsmr.localBounds = ssmr.localBounds;
                    dsmr.updateWhenOffscreen = true;    // overlay で skinning 維持（既存手と同条件）
                    outRenderer = dsmr;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; // mesh 共有
                    outRenderer = go.AddComponent<MeshRenderer>();
                }

                // submesh 数ぶん同一 outlineMat（色は単一）。
                int subs = Mathf.Max(1, mesh.subMeshCount);
                var mats = new Material[subs];
                for (int i = 0; i < subs; i++) mats[i] = outlineMat;
                outRenderer.sharedMaterials = mats;
                outRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outRenderer.receiveShadows = false;
                h.OutlineRenderers.Add(outRenderer);
            }
            Plugin.Log.LogInfo($"[ControllerModel] アウトライン構築（{kind}・renderer={h.OutlineRenderers.Count}）");
        }

        /// <summary>アウトラインの色/太さ（live）と表示 ON/OFF を反映する（全種別から呼ぶ＝非対象は OutlineMat==null で no-op）。
        /// 色/太さは毎フレ OutlineMat に書く（F10 live）。enable は構築時保持の OutlineRenderers の enabled を切替
        /// （毎フレ GetComponentsInChildren しない）。visible=本体表示状態（snap.Valid && Mat 有）＝本体非表示で輪郭も消す。</summary>
        private static void ApplyOutline(HandState h, HandModelKind kind, bool visible)
        {
            if (h.OutlineMat == null) return; // 非対象種別 / 未 bake
            bool enabled;
            Color col;
            float width;
            if (kind == HandModelKind.Hand)
            {
                enabled = Configs.HandOutlineEnabled.Value;
                col = new Color(Configs.HandOutlineColorR.Value, Configs.HandOutlineColorG.Value, Configs.HandOutlineColorB.Value, 1f);
                width = Configs.HandOutlineWidth.Value;
            }
            else // Tambourine / GlowStick
            {
                enabled = Configs.KaraokePropOutlineEnabled.Value;
                col = new Color(Configs.KaraokePropOutlineColorR.Value, Configs.KaraokePropOutlineColorG.Value, Configs.KaraokePropOutlineColorB.Value, 1f);
                width = Configs.KaraokePropOutlineWidth.Value;
            }
            h.OutlineMat.SetColor(s_outlineColorId, col);
            h.OutlineMat.SetFloat(s_outlineWidthId, width);

            bool show = visible && enabled;
            var list = h.OutlineRenderers;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) list[i].enabled = show;
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
            h.HandShadeBaseCaptured = false; // 素体影色の捕捉も無効化（次回 Build で再捕捉＝Cast 採取完了の反映に必須）
            h.OutlineMat = null;        // 輪郭マテリアルは OwnedMats で破棄済み・輪郭子 GO は h.Go 道連れ破棄
            h.OutlineRenderers.Clear(); // 次回 BuildOutline で再生成（fake-null 参照を残さない）
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

            // 手モデルは ERISA Babydoll の素体マテリアルを優先採取（HandToonOverlay へ property コピー）。
            //（ベース sentinel から per-hand コピー＝Ready のときのみ成功）。
            // 未完了/失敗時は false を返し下の unlit fallback に落ちる（Home 起動直後の数秒の不変条件）。
            if (kind == HandModelKind.Hand)
            {
                if (HandSkinMaterialResolver.TryResolve(out Material castMat, out Color castBase))
                {
                    Plugin.Log.LogInfo($"[ControllerModel] {kind}/{hand} ERISA素体マテリアルから構築（shader={castMat.shader.name}）");
                    baseColor = castBase;
                    return castMat;
                }
                // 採取未完了 or 失敗 → 既存 unlit 経路へ
            }

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
        private static void AssignColorDrivenMaterials(HandState h, bool toon)
        {
            // toon = カラオケ楽器（タンバリン/サイリウム）はアニメ調 HandToonOverlay / それ以外（Cheki カメラ）は従来 unlit。
            Shader bundled = toon ? BundledShaders.HandToonOverlay : BundledShaders.ControllerUnlit;
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
                        m = toon ? CreateToonTinted(shader, bundled != null, c)
                                 : CreateUnlitTinted(shader, bundled != null, c);
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

        /// <summary>テクスチャ無し・単色 tint のアニメ調 toon マテリアルを 1 枚作る（カラオケ楽器 submesh 用）。
        /// 手と同じ HandToonOverlay（2-tone shade / rim / global light _BG2VR_HandLight*）を流用する。
        /// 影色 _1st_ShadeColor は base の自己暗色化（中立グレーを避け元色が分かるコントラスト）。matcap は使わない。
        /// 前面ポリシーは BuildMaterial と同じ（ZTest LEqual + LaserQueue）。プロップは閉じたソリッド・mirror しない＝Cull Back。
        /// 発光（_EmissionColor）は既定 0 のまま＝ApplyGlow が GlowStick の発光部だけ毎フレ書く。shader 不在なら null。</summary>
        private static Material CreateToonTinted(Shader shader, bool bundled, Color color)
        {
            if (shader == null) return null;
            Material mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.color = color; // mainTexture なし＝_Color の単色
            if (bundled)
            {
                mat.SetColor("_1st_ShadeColor", ControllerModelPose.Brightened(color, Configs.KaraokePropShadeFactor.Value)); // 影=素色×係数（初期値・ApplyToonShade が live 更新）
                mat.SetFloat("_ShadeFeather", Configs.KaraokePropShadeFeather.Value); // 影境界フェード（初期値・ApplyToonShade が live 更新）
                mat.SetFloat("_MatCap_Intensity", 0f);                    // プロップは matcap なし（手と差別化）
                // rim は shader 既定が手用の暖色（肌色 tone）＝プロップには不適。a=0 で無効化し 2-tone のみの
                // クリーンな toon にする（material のビジュアル要素を全て明示制御＝手用既定の継承を断つ）。
                mat.SetColor("_RimColor", new Color(0f, 0f, 0f, 0f));
                mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back); // 閉じたソリッド＝Back（winding 正）
                mat.SetFloat("_ZWrite", 1f);
                mat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;
            }
            else
            {
                // bundle 不在の degrade（UI/Default 等）。toon にならないが手元が空にならない最終手段。
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
