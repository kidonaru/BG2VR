using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using BG2VR.UiSceneVoid;

namespace BG2VR.VrInput
{
    /// <summary>
    /// Cheki 撮影中(PHOTOGRAPHING)のみ、専用 Camera で右手コントローラ姿勢のシーン視界を正方 RenderTexture へ描画し、
    /// instax 背面 quad に live 表示する（物理照準＝カメラが物理的にコントローラを向く）。RT は Phase 3 の撮影 patch が
    /// 写真にも使う（WYSIWYG の単一真値）。snapshot は ProjectorRunner 単一読取点から受領（二重読取規約）。
    /// カメラ/RT/quad は遅延生成し、Cheki 終了で Teardown 破棄。fork 非依存（全て BG2VR 側オブジェクト）。
    /// </summary>
    internal sealed class ChekiCameraRunner
    {
        // 撮影 patch（static 文脈）が現在の RT を引くための橋渡し。ProjectorRunner が単一インスタンスを保持する前提。
        private static ChekiCameraRunner s_instance;

        // 撮影確定(シャッター)後、ゲームは m_state を PHOTOGRAPHING_DONE へ遷移してから await DelayFrame(1) を挟んで
        // GetChekiTexture を呼ぶ（Cheki.updatePhotographing → GBSystem.SaveCheki 実測）。その時点で
        // IsChekiPhotographing は false ＝ Tick(active:false) で m_wasActive が落ちている。そのため active 連動の
        // m_wasActive だけでは RT を引けず差し替えが不発になる。シャッター rising edge で m_captureLatch をセットし、
        // GetChekiTexture に消費されるまで RT を生かす（camera は Deactivate で停止＝RT はシャッター瞬間の VF で凍結）。
        public static RenderTexture ActiveRT =>
            (s_instance != null && (s_instance.m_wasActive || s_instance.m_captureLatch > 0)
             && s_instance.m_rt != null && s_instance.m_rt.IsCreated())
                ? s_instance.m_rt : null;

        /// <summary>
        /// active な runner の VR カメラを size² で再レンダリングし、読み取り可能 Texture2D を返す static facade
        /// （ActiveRT と同じ s_instance 委譲）。撮影直後の latch 中（GetChekiTexture Postfix）に呼ばれる前提。
        /// null = 不可（非アクティブ / s_instance 不在 / cam 不在）。FixMod 高解像度チェキへの hi-res 供給に使う。
        /// </summary>
        internal static Texture2D RenderHiResForActive(int size)
        {
            var inst = s_instance;
            if (inst == null) return null;
            return inst.RenderHiRes(size);
        }

        // シャッター後、PHOTOGRAPHING_DONE→await DelayFrame(1)→GetChekiTexture までの猶予フレーム数。
        // DelayFrame(1) + CaptureCheki の WaitForEndOfFrame + Update 実行順の揺れを十分カバーする構造的タイミング定数。
        private const int CaptureLatchFrames = 8;

        private Camera m_cam;
        private RenderTexture m_rt;
        private GameObject m_screen;       // 背面 quad
        private Material m_screenMat;
        private bool m_wasActive;
        private bool m_warned;
        private bool m_prevTrigger;        // シャッター rising edge
        private int m_captureLatch;        // シャッター後 GetChekiTexture 消費まで RT を生かす残フレーム

        public ChekiCameraRunner() { s_instance = this; }

        /// <param name="active">撮影中（ShowChekiCamera && IsChekiPhotographing && camHand.Valid）</param>
        public void Tick(Transform rig, in VrControllerSnapshot camHand, bool active)
        {
            try { TickInner(rig, camHand, active); }
            catch (System.Exception e)
            {
                if (!m_warned) { m_warned = true; Plugin.Log.LogWarning($"[ChekiCam] 想定外エラーで停止: {e}"); }
                Deactivate();
            }
        }

        private void TickInner(Transform rig, in VrControllerSnapshot camHand, bool active)
        {
            if (!active)
            {
                Deactivate();
                m_prevTrigger = false;
                ChekiInputBridge.FreezeAim = false;
                if (m_captureLatch > 0) m_captureLatch--; // 撮影確定後の GetChekiTexture 消費まで RT を温存
                // Cheki 自体が終了 → カメラ/RT/quad を破棄（撮影確定直後の latch 中は GetChekiTexture 未消費の恐れ＝温存）
                if (!MiniGameProbe.IsCheki() && m_captureLatch <= 0) Teardown();
                return;
            }

            EnsureCreated();
            if (m_cam == null) { Deactivate(); return; } // 生成失敗 → 撮影 patch はフォールスルー

            int res = Mathf.Clamp(Configs.ChekiRtResolution.Value, 128, 1024);
            if (m_rt == null || m_rt.width != res) RebuildRt(res);

            // cullingMask: ~0 から VR visuals 2 層（UI/レティクル/レーザー=30 / コントローラ・instax 本体=29）を除外
            //（手元モデル/手/画面/レーザーを写真に写し込まない）。
            // base に scene.cullingMask を使わないのは EyeCullingCoordinator が毎フレ上書きするため。
            m_cam.cullingMask = VrCameraMask.Exclude(~0, VrLayers.Visuals, VrLayers.VisualsPostProcessed);

            // カメラ pose = コントローラ手元 + offset（レーザーピッチ非適用＝実機 1:1。model と同じ Compute）。
            Vector3 posOffset = new Vector3(
                Configs.ChekiCamPosOffsetX.Value, Configs.ChekiCamPosOffsetY.Value, Configs.ChekiCamPosOffsetZ.Value);
            Quaternion rotOffset = Quaternion.Euler(
                Configs.ChekiCamRotOffsetX.Value, Configs.ChekiCamRotOffsetY.Value, Configs.ChekiCamRotOffsetZ.Value);
            ControllerModelPose.Compute(camHand.RigLocalPosition, camHand.RigLocalRotation, posOffset, rotOffset,
                out Vector3 camLocalPos, out Quaternion camLocalRot);
            if (m_cam.transform.parent != rig) m_cam.transform.SetParent(rig, false);
            m_cam.transform.localPosition = camLocalPos;
            m_cam.transform.localRotation = camLocalRot;
            m_cam.enabled = true; // targetTexture 持ち＝毎フレ自動描画

            // post-process（グレーディング+Bloom）をゲーム同値で反映（VrGamePostProcess に追従）。
            // 参照＝ゲームカメラの ACD をコピー。AA 専用処理は持たず pipeline msaaSampleCount（EyeMsaaRunner が
            // VR 中 VrEyeMsaa へ駆動）に委ね、post 有効化で URP 中間バッファ経路に乗せて resolve させる。
            if (Configs.VrGamePostProcess.Value)
                ChekiCameraEffects.ApplyPostProcess(m_cam, CameraFinder.FindGameCamera());
            else
                ChekiCameraEffects.DisablePostProcess(m_cam);

            // 背面スクリーン quad: カメラ本体に剛体マウントする。位置/回転ともカメラの解決済み pose
            // (camLocalPos/camLocalRot) 基準＝画面 offset はカメラフレームで解釈される。手フレーム基準だと
            // ChekiCamRotOffset でカメラを傾けたとき画面が本体から外れて「下」に出る（実機指摘 2026-06-13）。
            // ChekiScreenRotOffset で画面を自分の方へ向けて調整する（カメラの光軸とは別に角度を付けられる）。
            Vector3 scrOffset = new Vector3(
                Configs.ChekiScreenPosOffsetX.Value, Configs.ChekiScreenPosOffsetY.Value, Configs.ChekiScreenPosOffsetZ.Value);
            Quaternion scrRotOffset = Quaternion.Euler(
                Configs.ChekiScreenRotOffsetX.Value, Configs.ChekiScreenRotOffsetY.Value, Configs.ChekiScreenRotOffsetZ.Value);
            if (m_screen.transform.parent != rig) m_screen.transform.SetParent(rig, false);
            m_screen.transform.localPosition = camLocalPos + camLocalRot * scrOffset;
            m_screen.transform.localRotation = camLocalRot * scrRotOffset;
            float s = Configs.ChekiScreenSize.Value;
            m_screen.transform.localScale = new Vector3(s, s, s);
            m_screen.SetActive(camHand.Valid);

            // ズーム: カメラ手スティックY → FOV（rate+clamp）。アクティブ化の最初のフレームで既定にリセット。
            if (!m_wasActive) ChekiZoom.CurrentFov = Configs.ChekiCamFovDefault.Value;
            ChekiZoom.CurrentFov = ChekiZoom.Step(
                ChekiZoom.CurrentFov, camHand.Stick.y, Configs.ChekiZoomSpeed.Value, Time.deltaTime,
                Configs.ChekiCamFovMin.Value, Configs.ChekiCamFovMax.Value);
            m_cam.fieldOfView = ChekiZoom.CurrentFov;

            // 視点固定: 撮影中はゲームの CameraControll を 0 に（VF カメラが照準を担う）。
            ChekiInputBridge.FreezeAim = true;

            // シャッター: カメラ手トリガー rising edge → 既存 LeftClick 注入（Cheki は ATriggered||LeftClick で発火）。
            // ATriggered への二重 Postfix を避け、read=consume 実績のある GbInputBridge.LeftClickPulse を再利用。
            bool trig = camHand.Trigger;
            if (trig && !m_prevTrigger)
            {
                GbInputBridge.LeftClickPulse = true;
                // PHOTOGRAPHING_DONE 中に呼ばれる GetChekiTexture まで RT を生かす（active が落ちても ActiveRT 非 null）。
                m_captureLatch = CaptureLatchFrames;
            }
            m_prevTrigger = trig;

            m_wasActive = true;
        }

        private void EnsureCreated()
        {
            if (m_cam == null)
            {
                var go = new GameObject("BG2VR_ChekiCam") { hideFlags = HideFlags.HideAndDontSave };
                m_cam = go.AddComponent<Camera>();
                m_cam.clearFlags = CameraClearFlags.SolidColor;
                m_cam.backgroundColor = Color.black;
                m_cam.nearClipPlane = 0.03f;
                m_cam.farClipPlane = 1000f;
                m_cam.fieldOfView = ChekiZoom.CurrentFov;
                m_cam.enabled = false;
                RebuildRt(Mathf.Clamp(Configs.ChekiRtResolution.Value, 128, 1024));
            }
            if (m_screen == null)
            {
                m_screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
                m_screen.name = "BG2VR_ChekiScreen";
                m_screen.hideFlags = HideFlags.HideAndDontSave;
                Object.Destroy(m_screen.GetComponent<Collider>());
                m_screen.layer = VrLayers.Visuals; // UiSceneVoid 中も eye 可視
                Shader sh = BundledShaders.ControllerUnlit;
                m_screenMat = sh != null ? new Material(sh) : new Material(Shader.Find("UI/Default"));
                m_screenMat.hideFlags = HideFlags.HideAndDontSave;
                if (sh != null)
                {
                    // 背面スクリーンは最前面（ZTest Always＝shader 既定）。手元の表示画面は常にプレイヤーに見えるべきで、
                    // 何にも遮蔽されない（レティクル/設定パネルと同じ frontmost ポリシー）。LessEqual にすると選択的深度
                    // プリパス（VrControllerOccludeUi）の occluder（layer 29＝instax 本体）に隠れ、かつ reversed-Z
                    // （D3D12, near=1/far=0）では LessEqual の比較方向が depth buffer の向きに対し逆転し、far=0 へ
                    // clear した buffer に全フラグメントが test fail＝全面消失する。
                    m_screenMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                    m_screenMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    m_screenMat.renderQueue = BG2VR.WorldUi.UiOverlayRenderPolicy.LaserQueue;
                }
                if (m_rt != null) m_screenMat.mainTexture = m_rt;
                var mr = m_screen.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m_screenMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_screen.SetActive(false);
            }
        }

        private void RebuildRt(int res)
        {
            if (m_rt != null) { if (m_cam != null) m_cam.targetTexture = null; m_rt.Release(); Object.Destroy(m_rt); }
            m_rt = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32) { name = "BG2VR_ChekiRT" };
            m_rt.Create();
            if (m_cam != null) m_cam.targetTexture = m_rt;
            if (m_screenMat != null) m_screenMat.mainTexture = m_rt;
        }

        /// <summary>
        /// m_cam を size² の一時 RT へ手動レンダリングし Texture2D（R8G8B8A8_SRGB, mips なし）を返す。
        /// VF（m_rt・320 経路）より高解像度の WYSIWYG 写真を FixMod hi-res 経路へ供給するため。
        ///
        /// <para>不変条件: m_cam の pose / cullingMask / targetTexture は active 時の TickInner でのみ書かれ、
        /// Deactivate はそれらを据え置く。よってシャッター後（latch 中・MonoBehaviour Update と GetChekiTexture
        /// を呼ぶ UniTask 継続の相対順序が非保証）に呼ばれても、シャッター時の構図で再レンダされる。
        /// ※将来 Tick(active:false) 経路が pose / cullingMask / targetTexture を触る変更を入れると本前提が壊れる。</para>
        ///
        /// <para>latch（m_captureLatch）/ m_wasActive 等の状態は一切変更しない純レンダ＝再入しても sidecar/latch を壊さない。
        /// targetTexture / RenderTexture.active は finally で必ず復元（漏れると背面スクリーン quad が黒化する）。</para>
        /// </summary>
        private Texture2D RenderHiRes(int size)
        {
            if (m_cam == null) return null;
            RenderTexture tmp = null;
            RenderTexture prevTarget = m_cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                tmp = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                m_cam.targetTexture = tmp;
                m_cam.Render();
                RenderTexture.active = tmp;
                var tex = new Texture2D(size, size, GraphicsFormat.R8G8B8A8_SRGB, 0, TextureCreationFlags.None);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                tex.Apply(false);
                return tex;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[ChekiCam] hi-res 再レンダ失敗・FixMod スクショへフォールバック: {e.Message}");
                return null;
            }
            finally
            {
                m_cam.targetTexture = prevTarget; // = m_rt（VF を壊さない）
                RenderTexture.active = prevActive;
                if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
            }
        }

        private void Deactivate()
        {
            m_wasActive = false;
            if (m_cam != null) m_cam.enabled = false;
            if (m_screen != null) m_screen.SetActive(false);
        }

        /// <summary>Cheki 終了時にカメラ/RT/quad/material を破棄（常駐リーク防止）。</summary>
        private void Teardown()
        {
            Deactivate();
            if (m_cam != null) { m_cam.targetTexture = null; Object.Destroy(m_cam.gameObject); m_cam = null; }
            if (m_rt != null) { m_rt.Release(); Object.Destroy(m_rt); m_rt = null; }
            if (m_screen != null) { Object.Destroy(m_screen); m_screen = null; }
            if (m_screenMat != null) { Object.Destroy(m_screenMat); m_screenMat = null; }
        }
    }
}
