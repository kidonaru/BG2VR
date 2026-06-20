using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityVRMod.Core;

namespace BG2VR.PostProcess
{
    /// <summary>
    /// ゲームの URP post-process（カラーグレーディング + Bloom）を VR 両眼へ反映する policy 所有者。
    ///
    /// 仕組み（EyeCullingCoordinator と同型の override push）:
    ///  1. アクティブな global PPE Volume を列挙し、その存在 layer の和集合を eye の volumeLayerMask として
    ///     <see cref="VRModCore.SetEyePostProcessOverride"/> で push（fork が RenderEye で eye の
    ///     renderPostProcessing=true + volumeLayerMask を適用＝ゲームと同じ Volume を eye が拾う）。
    ///  2. DepthOfField / ChromaticAberration /（config で）Vignette の VolumeComponent.active を VR 中のみ
    ///     false にする（共有 global Volume を直接変異＝デスクトップミラーも同様に外れるが VR プレイ中は許容）。
    ///     元値は per-instance キャッシュし、無効化/VR 終了/破棄/シーン遷移で確実に復元する（単一所有点
    ///     <see cref="RestoreAll"/>）。
    ///  3. Bloom の intensity を config 倍率（VrPostProcessBloomScale）で VR 中のみ orig×scale に書き換える
    ///     （同じく共有 Volume 直接変異・元値キャッシュ・<see cref="RestoreAll"/> で復元）。
    ///     【回帰ゼロ不変条件】既定 1.0（<see cref="PostProcessPolicy.ShouldApplyBloomScale"/>==false）のときは
    ///     一切書き込まない＝完全 no-op（毎フレ orig×1.0 を書くとゲームの bloom アニメに対し stale orig へ pin
    ///     して回帰する）。dirty フラグで「1.0 へ戻した瞬間に 1 度だけ native 復元」する。
    ///     既知限界（scale≠1.0 時のみ）: 同一シーン内でゲームが bloom intensity をアニメ変更しても settle 時
    ///     キャッシュ値基準で override される（シーン遷移 settle で comp 単位に再キャッシュ＝シーン内アニメに限定）。
    ///
    /// URP（Volume / VolumeComponent）型は BG2VR が直参照しないため EyeMsaaRunner と同じく reflection で扱う。
    /// Volume 列挙（FindObjectsByType）はシーン遷移直後の短い settle 窓のみ行い、定常フレームはキャッシュ済み
    /// component の active を再適用するだけ＝毎フレ全列挙を避ける。
    ///
    /// 既知の限界: 列挙は scene load/unload 起点の settle 窓に限定されるため、同一シーン内で scene load を
    /// 伴わずに後出しで active 化/無効化された global PPE Volume は次の遷移まで拾わない（その Volume の
    /// グレーディングは eye に反映されず、DoF/CA 抑制も掛からない）。VR/desktop で漏れが実機観測されたら
    /// settle 非依存の低頻度再列挙へ拡張する（予防コードは入れない方針＝観測まで現状維持）。
    /// </summary>
    internal sealed class PostProcessCoordinator : MonoBehaviour
    {
        // シーン遷移後、Volume が active 化するまでの猶予として列挙を続けるフレーム数（短い保険）。
        private const int ReEnumSettleFrames = 10;

        // 抑制中 component（VolumeComponent インスタンス）→ 抑制前の active 値。復元の真値源。
        private readonly Dictionary<Object, bool> m_suppressed = new();
        // Bloom component → settle 時 intensity（倍率適用前の真値）。復元の真値源。
        private readonly Dictionary<Object, float> m_bloomOrig = new();
        // Bloom を現在 override 書込中か（dirty のときだけ復元する＝既定 1.0 で書込ゼロを保つ）。
        private bool m_bloomDirty;
        // 列挙時の作業バッファ（GC 回避のため再利用）。
        private readonly HashSet<Object> m_seen = new();
        private readonly List<Object> m_removeBuf = new();

        private int m_eyeVolumeMask;
        private int m_settleFrames;
        private bool m_active;          // override を出している最中か（false への遷移で RestoreAll する）
        private bool m_lastKeepVignette;
        private bool m_loggedActive;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Teardown(); // 無効化/破棄でも抑制を残置しない（共有 Volume の復元保証・単一所有点）。
        }

        private void OnDestroy() => Teardown();

        // シーン（env 含む additive）の load/unload で Volume 構成が変わりうる → 短い settle 窓で再列挙。
        private void OnSceneLoaded(Scene s, LoadSceneMode m) => m_settleFrames = ReEnumSettleFrames;
        private void OnSceneUnloaded(Scene s) => m_settleFrames = ReEnumSettleFrames;

        private void Update()
        {
            // 機能 OFF / VR 非描画中は override を解除し抑制を復元して退く（RestoreAll を必ず通す）。
            if (!PostProcessPolicy.ShouldOverride(Configs.VrGamePostProcess.Value, VRModCore.IsVrActive))
            {
                Deactivate();
                return;
            }

            bool keepVignette = Configs.VrPostProcessVignette.Value;

            // 列挙トリガ: 初回アクティブ化 / シーン遷移 settle 窓 / Vignette 設定変更（抑制対象が変わる）。
            bool firstActivate = !m_active;
            bool vignetteChanged = m_active && keepVignette != m_lastKeepVignette;
            if (firstActivate) m_settleFrames = ReEnumSettleFrames;

            if (m_settleFrames > 0 || vignetteChanged)
            {
                ReEnumerate(keepVignette);
                if (m_settleFrames > 0) m_settleFrames--;
                if (m_eyeVolumeMask != 0) m_settleFrames = 0; // Volume を捕捉できたら settle 終了
            }

            m_lastKeepVignette = keepVignette;
            m_active = true;

            // 定常: キャッシュ済み抑制 component の active=false を毎フレ保証（ゲーム/他が戻しても収束）。
            ReapplySuppression();
            // Bloom intensity を config 倍率で毎フレ反映（F10 スライダー live）。既定 1.0 は no-op。
            ReapplyBloom();

            // global PPE Volume が見つかっていれば eye に post-process を効かせる（mask=0 なら反映対象なし）。
            // overlay mask: UI/レティクル/Cheki 画面（layer 30=Visuals）を post から外す（KeepUiCrisp ON 時）。
            // active=false（mask=0）のときは fork 側で overlay 除外も無効＝従来描画になる。
            // 【不変条件】overlayMask に VisualsPostProcessed(29) を絶対に含めないこと。29 はレーザー/コントローラ
            //  ＝main pass に残して post を乗せる層であり、ここに入れると「main pass で post 描画」＋「DrawEyeOverlay
            //  で crisp 重ね描き」の二重描画になる（fork は overlayMask 一致 layer のみ post 後に重ね描く）。
            int overlayMask = Configs.VrPostProcessKeepUiCrisp.Value ? VrLayers.VisualsMask : 0;
            VRModCore.SetEyePostProcessOverride(m_eyeVolumeMask != 0, m_eyeVolumeMask, overlayMask);

            // コントローラ遮蔽（VrControllerOccludeUi）: overlay 機構が走る条件（post override + UI crisp）と
            // 同一のときだけ、遮蔽源を「コントローラ層(29)のみ」にして UI を depth test（LEqual）させる
            // （机/キャラ等シーンは遮蔽源にしない＝選択的深度）。fork が UI 描画直前に深度カーブを行う。
            bool occlude = Configs.VrControllerOccludeUi.Value && m_eyeVolumeMask != 0 && Configs.VrPostProcessKeepUiCrisp.Value;
            Material depthMat = occlude ? ResolveDepthMaterial() : null;
            VRModCore.SetEyeOverlayOccluder(occlude ? VrLayers.VisualsPostProcessedMask : 0, depthMat);

            if (!m_loggedActive)
            {
                Plugin.Log.LogInfo($"[PostProcess] VR eye に post-process 反映 (volumeLayerMask={m_eyeVolumeMask}, keepVignette={keepVignette}, suppressed={m_suppressed.Count})。");
                m_loggedActive = true;
            }
        }

        // override を解除し抑制を復元する（毎フレ呼ばれても二重実行しない）。
        private void Deactivate()
        {
            if (!m_active) return;
            Teardown();
        }

        private void Teardown()
        {
            VRModCore.SetEyePostProcessOverride(false, 0, 0);
            VRModCore.SetEyeOverlayOccluder(0, null); // コントローラ遮蔽も解除（fork は次フレ push が無ければ深度カーブ無し）。
            RestoreAll();
            m_eyeVolumeMask = 0;
            m_settleFrames = 0;
            m_active = false;
            if (m_loggedActive)
            {
                Plugin.Log.LogInfo("[PostProcess] VR eye の post-process 反映を解除（抑制を復元）。");
                m_loggedActive = false;
            }
        }

        // アクティブ global PPE Volume を列挙し、抑制対象 component のキャッシュ/抑制集合と eye mask を更新する。
        private void ReEnumerate(bool keepVignette)
        {
            m_seen.Clear();
            int mask = 0;

            System.Type volType = ResolveVolumeType();
            if (volType != null)
            {
                Object[] volumes = Object.FindObjectsByType(volType, FindObjectsSortMode.None);
                foreach (var volObj in volumes)
                {
                    if (volObj is not Behaviour beh || !beh.isActiveAndEnabled) continue;
                    if (GetMember(volObj, "isGlobal") is not bool isGlobal || !isGlobal) continue;
                    object profile = GetMember(volObj, "sharedProfile");
                    if (profile == null) continue;
                    if (GetMember(profile, "components") is not System.Collections.IList comps) continue;

                    // この global PPE Volume の layer を eye mask へ（eye がグレーディング+Bloom を拾う）。
                    mask = PostProcessPolicy.AddLayer(mask, ((Component)volObj).gameObject.layer);

                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i] is not Object comp) continue;
                        // Bloom は抑制対象でなく intensity を倍率で書き換える対象＝orig をキャッシュ（順序: 同フレで
                        // ReEnumerate は ReapplyBloom より前に走る＝新規 comp は override 前の native 値を確定できる）。
                        if (comp.GetType().Name == "Bloom")
                        {
                            m_seen.Add(comp);
                            // reflection 解決成功時のみ orig 登録（失敗時に 0 を焼いて復元で消灯させない）。
                            if (!m_bloomOrig.ContainsKey(comp) && TryGetBloomIntensity(comp, out float orig))
                                m_bloomOrig[comp] = orig;
                            continue;
                        }
                        if (!PostProcessPolicy.ShouldSuppress(comp.GetType().Name, keepVignette)) continue;
                        m_seen.Add(comp);
                        if (!m_suppressed.ContainsKey(comp))
                            m_suppressed[comp] = GetActive(comp); // 抑制前の active を記録
                    }
                }
            }

            // sweep: 今回 target でなくなった component（Vignette を残す設定へ変更 / Volume 消滅）を復元・除去。
            if (m_suppressed.Count > 0)
            {
                m_removeBuf.Clear();
                foreach (var kv in m_suppressed)
                    if (!m_seen.Contains(kv.Key)) m_removeBuf.Add(kv.Key);
                foreach (var key in m_removeBuf)
                {
                    // TryGetValue で引く（indexer は不可）。key は UnityEngine.Object＝シーン遷移で managed ラッパは
                    // != null のまま辞書ルックアップが外れる staleness があり、indexer だと KeyNotFound を throw して
                    // ReEnumerate を中断＝settle 減算前に抜けて毎フレーム再発する。引けなくても Remove は必ず通す。
                    if (key != null && m_suppressed.TryGetValue(key, out bool wasActive)) SetActive(key, wasActive); // 生存していれば元値へ復元
                    m_suppressed.Remove(key);
                }
            }

            // Bloom sweep: 今回 seen でない Bloom comp は（dirty なら生存時に orig へ戻してから）除去。
            // 全 Bloom が消えたら dirty を実態（override 対象なし）へ収束させる（stale dirty 残留を防ぐ）。
            if (m_bloomOrig.Count > 0)
            {
                m_removeBuf.Clear();
                foreach (var kv in m_bloomOrig)
                    if (!m_seen.Contains(kv.Key)) m_removeBuf.Add(kv.Key);
                foreach (var key in m_removeBuf)
                {
                    // unload で seen 落ちした生存 comp は settle 時 orig を pop する（scale≠1.0 のシーン内アニメに限る既知限界）。
                    // indexer 不可・TryGetValue で引く（Object キー staleness の理由は上の suppress sweep 参照。
                    // dirty 時のみ orig 復元。Cheki 突入で毎フレ throw の実害）。引けなくても Remove は必ず通す。
                    if (key != null && m_bloomDirty && m_bloomOrig.TryGetValue(key, out float orig)) SetBloomIntensity(key, orig);
                    m_bloomOrig.Remove(key);
                }
                if (m_bloomOrig.Count == 0) m_bloomDirty = false;
            }

            m_eyeVolumeMask = mask;
        }

        // 抑制中 component の active=false を毎フレ保証する（小集合＝低コスト）。
        private void ReapplySuppression()
        {
            if (m_suppressed.Count == 0) return;
            foreach (var kv in m_suppressed)
                if (kv.Key != null) SetActive(kv.Key, false);
        }

        // Bloom intensity を config 倍率で毎フレ反映（F10 live）。既定 1.0 は書き込まず、直前まで
        // override していたら（dirty）1 度だけ native（orig）へ戻して dirty 解除＝以後ゲームに委ねる。
        private void ReapplyBloom()
        {
            if (m_bloomOrig.Count == 0) return;
            float scale = Configs.VrPostProcessBloomScale.Value;
            if (PostProcessPolicy.ShouldApplyBloomScale(scale))
            {
                foreach (var kv in m_bloomOrig)
                    if (kv.Key != null)
                        SetBloomIntensity(kv.Key, PostProcessPolicy.ScaledBloomIntensity(kv.Value, scale));
                m_bloomDirty = true;
            }
            else if (m_bloomDirty)
            {
                foreach (var kv in m_bloomOrig)
                    if (kv.Key != null) SetBloomIntensity(kv.Key, kv.Value);
                m_bloomDirty = false;
            }
        }

        // 抑制と Bloom を共に元値へ戻す（復元の単一所有点）。独立 2 復元＝片方が空でももう片方を必ず通す。
        private void RestoreAll()
        {
            RestoreSuppressed();
            RestoreBloom();
        }

        // 抑制中の全 component を元の active 値へ戻す。fake-null は skip（既に破棄）。
        private void RestoreSuppressed()
        {
            if (m_suppressed.Count == 0) return;
            foreach (var kv in m_suppressed)
                if (kv.Key != null) SetActive(kv.Key, kv.Value);
            m_suppressed.Clear();
        }

        // Bloom を元の intensity へ戻す（dirty のときのみ書込＝1.0 運用で一度も触っていなければ書かない）。
        private void RestoreBloom()
        {
            if (m_bloomOrig.Count == 0) return;
            if (m_bloomDirty)
                foreach (var kv in m_bloomOrig)
                    if (kv.Key != null) SetBloomIntensity(kv.Key, kv.Value);
            m_bloomOrig.Clear();
            m_bloomDirty = false;
        }

        // ── reflection（URP 型を直参照しない。EyeMsaaRunner と同方針） ───────────────

        private System.Type m_volumeType;
        private bool m_volumeTypeResolved;

        private System.Type ResolveVolumeType()
        {
            if (m_volumeTypeResolved) return m_volumeType;
            m_volumeTypeResolved = true;
            m_volumeType = System.Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            if (m_volumeType == null)
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    m_volumeType = asm.GetType("UnityEngine.Rendering.Volume");
                    if (m_volumeType != null) break;
                }
            return m_volumeType;
        }

        // member 名ごとに PropertyInfo/FieldInfo を 1 度だけ解決してキャッシュ（property 優先・なければ field）。
        private readonly Dictionary<string, PropertyInfo> m_props = new();
        private readonly Dictionary<string, FieldInfo> m_fields = new();
        private readonly HashSet<string> m_memberResolved = new();

        private object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            if (m_memberResolved.Add(name))
            {
                System.Type t = obj.GetType();
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) m_props[name] = p;
                else
                {
                    FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                    if (f != null) m_fields[name] = f;
                }
            }
            if (m_props.TryGetValue(name, out var pi)) return pi.GetValue(obj);
            if (m_fields.TryGetValue(name, out var fi)) return fi.GetValue(obj);
            return null;
        }

        // VolumeComponent.active（基底 VolumeComponent の public bool field）。派生型から取得しても同一 field。
        private FieldInfo m_activeField;
        private bool m_activeFieldResolved;

        private FieldInfo ResolveActiveField(Object comp)
        {
            if (!m_activeFieldResolved)
            {
                m_activeFieldResolved = true;
                m_activeField = comp.GetType().GetField("active", BindingFlags.Public | BindingFlags.Instance);
            }
            return m_activeField;
        }

        private bool GetActive(Object comp)
        {
            FieldInfo f = ResolveActiveField(comp);
            return f == null || (f.GetValue(comp) is bool b && b);
        }

        private void SetActive(Object comp, bool value)
        {
            ResolveActiveField(comp)?.SetValue(comp, value);
        }

        // Bloom.intensity（public field・MinFloatParameter＝VolumeParameter<float>）と その value プロパティ。
        // GetActive/SetActive と同方針＝最初の comp で遅延解決しキャッシュ（Bloom 型は単一なので全 comp 共通）。
        private FieldInfo m_bloomIntensityField;
        private PropertyInfo m_bloomValueProp;
        private bool m_bloomReflResolved;

        private object GetIntensityParam(Object comp)
        {
            if (!m_bloomReflResolved)
            {
                m_bloomReflResolved = true;
                m_bloomIntensityField = comp.GetType().GetField("intensity", BindingFlags.Public | BindingFlags.Instance);
                object p = m_bloomIntensityField?.GetValue(comp);
                if (p != null)
                    m_bloomValueProp = p.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
            }
            return m_bloomIntensityField?.GetValue(comp);
        }

        // intensity を読めたら true（読めない＝reflection 未解決/型不一致では false＝呼び側が orig 登録を見送る）。
        private bool TryGetBloomIntensity(Object comp, out float value)
        {
            object p = GetIntensityParam(comp);
            if (p != null && m_bloomValueProp?.GetValue(p) is float f) { value = f; return true; }
            value = 0f;
            return false;
        }

        private void SetBloomIntensity(Object comp, float value)
        {
            object p = GetIntensityParam(comp);
            if (p != null) m_bloomValueProp?.SetValue(p, value);
        }

        // ── コントローラ遮蔽用 深度専用 material（BG2VR/DepthOnly） ───────────────
        // 同梱 shader bundle から 1 個だけ生成し全 coordinator で共有（hideFlags で hierarchy に出さない）。
        // bundle 未 bake / 非サポートで shader が無ければ null＝遮蔽は fork 側 guard で no-op（黒画面化しない）。
        private static Material s_depthMat;
        private static bool s_depthMatTried;

        private static Material ResolveDepthMaterial()
        {
            if (!s_depthMatTried)
            {
                s_depthMatTried = true;
                Shader sh = BG2VR.VrInput.BundledShaders.DepthOnly;
                if (sh != null)
                {
                    s_depthMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    // 深度カーブの ZTest は eye RT の depth 向きに合わせる（reversed-Z=GEqual / 非=LEqual）。
                    // UI 側の unity_GUIZTestMode（UiOverlayRenderPolicy.ZTestMode）と同方向＝同 buffer 上で整合する。
                    s_depthMat.SetInt("_ZTest", BG2VR.WorldUi.UiOverlayRenderPolicy.ZTestMode(true, SystemInfo.usesReversedZBuffer));
                }
                else
                    Plugin.Log.LogWarning("[PostProcess] BG2VR/DepthOnly shader が無い（bundle 未 bake?）。コントローラ遮蔽は無効。");
            }
            return s_depthMat;
        }
    }
}
