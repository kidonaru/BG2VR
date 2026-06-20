using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BG2VR.VrInput
{
    /// <summary>ERISA Babydoll 衣装の素体マテリアル（UTS Toon・`m_skin_s`）を Addressables 経由で
    /// 1 度だけ非同期ロードし、ハンドモデル用に調整した new Material をベースキャッシュとして保持する。
    ///
    /// 主要設計判断:
    /// - prefab は Instantiate せず GetComponentsInChildren で SMR を走る（メモリ最小）
    /// - 採取後 prefab handle を即 Release（コピー Material は src 独立＝fake-null にならない）
    /// - per-hand コピー（TryResolve ごとに new Material(s_cachedBase)）= 既存 DestroyState 寿命と独立
    /// - Ready 遷移時に s_readyToken++ で呼び出し側に自動再構築シグナル
    /// - Failed 1 回警告のみ・自動 retry なし（採取は実運用で成功する前提・失敗時は unlit fallback 固定）
    /// </summary>
    internal static class HandSkinMaterialResolver
    {
        // ERISA Babydoll prefab path（実機 bridge で Addressables ロード成功確認済・2026-06-19）。
        private const string DonorPrefabPath = "Character/PC04_Erisa/04_Babydoll/PC04_Erisa.prefab";
        private const string DonorSmrName = "mesh_skin_upper";
        private const string DonorShaderName = "Toon";
        // 手モデルの肌色（main tex を焼かず単色＋トゥーン陰影で描くため）。
        // 素体 BCM の代表色に近い暖色寄り（unlit fallback の HandSkinColor と同値・実機チューニング余地あり）。
        private static readonly Color HandSkinColor = new Color(1.0f, 0.85f, 0.74f, 1f);

        private enum State { Idle, Loading, Ready, Failed }
        private static State s_state = State.Idle;
        private static Material s_cachedBase;
        private static AsyncOperationHandle<GameObject> s_donorHandle; // 採取成功時に保持し続ける（shader の AssetBundle 参照を生かす）。Failed/Reset で Release。
        private static bool s_donorHandleValid; // s_donorHandle の生存フラグ（IsValid だけだと default(handle) の判定が曖昧）
        private static int s_readyToken; // Ready 遷移ごとに ++ する単調増加。呼び出し側で「自動再構築」判定に使う。
        private static bool s_warnedFailure;
        // 1x1 RGBA32 単色 tex（肌色を MainTex/BaseMap に焼くことでキャストと同じ計算経路にする＝
        // _Is_LightColor_Base=1 のままシーン光源色が乗りつつ、tex 色 × 光源色で自然な見た目）。
        private static Texture2D s_handTintTex;
        private static Color s_lastTintColor = new Color(-1f, -1f, -1f, -1f); // 「未設定」マーカー

        /// <summary>Ready 遷移を追跡するための単調増加トークン。Ready になるたびに +1 される。
        /// 呼び出し側は HandState に LastResolverToken を持ち、不一致を検出したら DestroyState を強制する。
        /// 初期値 0＝Idle 状態。Ready 1 回目で 1 になる。</summary>
        public static int ReadyToken => s_readyToken;

        /// <summary>肌色を 1x1 tex に焼く（変化検出 + 重複書き込み回避）。
        /// 呼び出し側は kind==Hand のとき毎フレ呼んで Config 変化を即時反映する。
        /// SetPixel/Apply は色変化時のみ走る＝同色なら no-op で軽い。</summary>
        public static void UpdateSkinColor(Color color)
        {
            if (s_handTintTex == null) return; // Ready 前は何もしない
            if (s_lastTintColor == color) return;
            s_lastTintColor = color;
            s_handTintTex.SetPixel(0, 0, color);
            s_handTintTex.Apply(false, false);
        }

        /// <summary>非同期ロードを開始する（多重ガード）。
        /// Idle のときに Addressables.LoadAssetAsync を呼び Loading 遷移。Loading/Ready/Failed は no-op。
        /// 毎フレ呼んで安全。`handTex` はベース構築時の main tex として焼く＝呼び出しごとに不変前提。</summary>
        public static void EnsureBegin(Texture2D handTex)
        {
            if (s_state != State.Idle) return;

            s_state = State.Loading;
            try
            {
                var op = Addressables.LoadAssetAsync<GameObject>(DonorPrefabPath);
                op.Completed += h => OnLoaded(h, handTex);
            }
            catch (Exception ex)
            {
                s_state = State.Failed;
                WarnOnce($"LoadAssetAsync 例外: {ex.Message}");
            }
        }

        private static void ReleaseDonorHandleIfAny()
        {
            if (!s_donorHandleValid) return;
            try { if (s_donorHandle.IsValid()) Addressables.Release(s_donorHandle); }
            catch { /* 解放二重呼び等は無視 */ }
            s_donorHandleValid = false;
            s_donorHandle = default;
        }

        private static void OnLoaded(AsyncOperationHandle<GameObject> op, Texture2D handTex)
        {
            // 採取結果が Ready で確定するまでは「持っているけど未承認」状態。
            // 失敗パスは finally で必ず Release・成功パスは Release せず保持。
            s_donorHandle = op;
            s_donorHandleValid = true;
            bool success = false;
            try
            {
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    s_state = State.Failed;
                    WarnOnce($"prefab ロード失敗 (status={op.Status})");
                    return;
                }
                var prefab = op.Result;
                var smrs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

                // 1st: 既知の SMR 名で探す
                SkinnedMeshRenderer skin = null;
                for (int i = 0; i < smrs.Length; i++)
                {
                    if (smrs[i] != null && smrs[i].name == DonorSmrName) { skin = smrs[i]; break; }
                }
                // フォールバック: Toon shader を持つ最初の SMR（prefab 構造変更耐性）
                if (skin == null || skin.sharedMaterial == null)
                {
                    for (int i = 0; i < smrs.Length; i++)
                    {
                        var s = smrs[i];
                        if (s == null) continue;
                        var mat = s.sharedMaterial;
                        if (mat == null || mat.shader == null) continue;
                        if (mat.shader.name == DonorShaderName) { skin = s; break; }
                    }
                }
                if (skin == null || skin.sharedMaterial == null)
                {
                    s_state = State.Failed;
                    WarnOnce($"prefab 内に肌マテリアル候補が見つからない (smr_count={smrs.Length})");
                    return;
                }
                var src = skin.sharedMaterial;
                var srcShaderName = src.shader != null ? src.shader.name : null;
                if (!HandSkinShaderClassifier.IsAcceptable(srcShaderName))
                {
                    s_state = State.Failed;
                    WarnOnce($"donor shader が採用不可 (name={srcShaderName ?? "(null)"})");
                    return;
                }

                // HandToonOverlay shader（URP 非依存 single-pass・CommandBuffer overlay 描画可能）に切替。
                // Cast の UTS Toon は UniversalForward pass で URP RenderPipeline context 必須＝fork DrawEyeOverlay の
                // CommandBuffer 経路で描画 fail（VR eye パスで真っ黒問題と同根）。HandToonOverlay は自前で 2-tone shade /
                // rim / matcap を持ち、Cast から property を copy することで見た目を維持する。
                //
                // 設計根拠（旧実装の _NormalMap 等クリア処理が脱落しているように見える点）:
                // 旧実装で _NormalMap / _Set_MatcapMask / _Set_HighColorMask / _Set_RimLightMask / _Outline_Sampler /
                // _ClippingMask / _StencilMode / _Is_NormalMap* / _OUTLINE_NML keyword 等を null クリアしていたのは、
                // UTS shader が **手 UV と素体 UV のミスマッチ** で陰影破綻するため。本実装では new Material(handShader) で
                // shader 自体を URP 非依存自作に置換＝**素体 UV 補助マップを参照しない**（HandToonOverlay は _MainTex +
                // _MatCap_Sampler のみ tex 入力）→ クリア処理は **構造的に不要**。_MatCap_Sampler のみ Cast から継承するが、
                // shader 側は view-space normal でサンプル＝手 UV に依存しない＝整合。
                Shader handShader = BG2VR.VrInput.BundledShaders.HandToonOverlay;
                if (handShader == null)
                {
                    s_state = State.Failed;
                    WarnOnce("BG2VR/HandToonOverlay shader が見つからない (bundle 未 bake?)");
                    return;
                }
                var baseMat = new Material(handShader) { hideFlags = HideFlags.HideAndDontSave, name = "BG2VR_HandSkinBase" };

                // 1x1 skin tint tex を MainTex に焼く（ApplyBrightness の _Color 別経路と独立）。
                if (s_handTintTex == null)
                {
                    s_handTintTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Point,
                        name = "BG2VR_HandSkinTint"
                    };
                    s_handTintTex.SetPixel(0, 0, HandSkinColor);
                    s_handTintTex.Apply(false, false);
                    s_lastTintColor = HandSkinColor;
                }
                baseMat.SetTexture("_MainTex", s_handTintTex);
                baseMat.SetColor("_Color", Color.white); // tex × _Color で skin 色を出す（ApplyBrightness が _Color を毎フレ書き換える）

                // Cast Material の Toon property を HandToonOverlay 互換 property へ copy（同名 property をそのまま転送）。
                if (src.HasProperty("_1st_ShadeColor")) baseMat.SetColor("_1st_ShadeColor", src.GetColor("_1st_ShadeColor"));
                if (src.HasProperty("_BaseColor_Step")) baseMat.SetFloat("_BaseColor_Step", src.GetFloat("_BaseColor_Step"));
                if (src.HasProperty("_RimColor")) baseMat.SetColor("_RimColor", src.GetColor("_RimColor"));
                if (src.HasProperty("_RimLight_Power")) baseMat.SetFloat("_RimLight_Power", src.GetFloat("_RimLight_Power"));
                if (src.HasProperty("_MatCap_Sampler"))
                {
                    Texture mc = src.GetTexture("_MatCap_Sampler");
                    if (mc != null) baseMat.SetTexture("_MatCap_Sampler", mc);
                }
                // MatCap intensity は cast に直接 1 対 1 の property が無い→1.0 既定で（自作 shader 既定値）。
                // ControllerModelRunner が Configs.HandMatCapIntensity を毎フレ反映（F10 live）。

                // Cull=Back で凹形状（指↔手のひら）の裏面を culling（裏面透け解消）。
                // mirror 手（negative X scale）も Cull=Back で正＝GL.invertCulling（overlay 描画区間で true）と
                // 負スケールの winding 反転が相殺し、左右とも同一設定でよい（per-hand 分岐・法線符号補正は不要・実機検証 2026-06-19）。
                baseMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
                // ZTest GreaterEqual＝reversed-Z（D3D far=0）で「近い方が勝つ」深度テスト。ZWrite On（shader 既定）と
                // 併用で手の指/手のひら等の自己オクルージョンが成立（奥のパーツが手前を上書きしない）。
                // ・LEqual は reversed-Z で near を弾き手が全消失（実機検証 2026-06-19）
                // ・Always は深度ソート不能で凹形状の裏面が透ける
                // → GreaterEqual + Cull Back なら depth バッファのクリア有無（occluder prepass）に依存せず solid 化。
                //   コントローラ(ControllerUnlit)が LEqual で動くのは凸形状+Cull Back で前面が重ならず ZTest 方向が無関係なため。
                baseMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.GreaterEqual);
                baseMat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;

                s_cachedBase = baseMat;
                s_state = State.Ready;
                s_readyToken++;
                success = true;
                Plugin.Log.LogInfo($"[HandSkinMatResolver] ERISA Babydoll 採取完了（shader={srcShaderName}, handTex={(handTex != null ? handTex.name : "なし")}, readyToken={s_readyToken}）");
            }
            catch (Exception ex)
            {
                s_state = State.Failed;
                WarnOnce($"OnLoaded 例外: {ex}");
            }
            finally
            {
                // 成功時は handle を保持し続ける（コピー Material の shader 参照は AssetBundle ライフタイムに紐付くため、
                // Release すると shader が Hidden/InternalErrorShader に化けて手がマゼンタになる＝実機検証 2026-06-19 で判明）。
                // 失敗時のみ Release してメモリを解放（採取は実運用で成功する前提・失敗時は unlit fallback 固定）。
                if (!success) ReleaseDonorHandleIfAny();
            }
        }

        /// <summary>Ready なら採取済ベースから new Material でコピーを返す。per-hand 寿命と独立。
        /// baseColor は HandSkinColor（明るさ倍率の base＝ApplyBrightness が _Color/_BaseColor に乗算）。</summary>
        public static bool TryResolve(out Material mat, out Color baseColor)
        {
            mat = null;
            baseColor = HandSkinColor;
            if (s_state != State.Ready || s_cachedBase == null) return false;
            mat = new Material(s_cachedBase) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private static void WarnOnce(string msg)
        {
            if (s_warnedFailure) return;
            s_warnedFailure = true;
            Plugin.Log.LogWarning($"[HandSkinMatResolver] {msg}");
        }
    }
}
