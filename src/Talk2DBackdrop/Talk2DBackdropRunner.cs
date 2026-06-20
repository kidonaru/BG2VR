using System.Collections.Generic;
using GB.Scene;
using UnityEngine;
using UnityVRMod.Core; // VRModCore（namespace は UnityVRMod.Core）

namespace BG2VR.Talk2DBackdrop
{
    /// <summary>
    /// Talk2DScene（同伴・デート等の 2D 背景イベント）の VR 見せ方調整（spec 2026-06-05）。
    /// ①背景 m_bg をカメラ基準で相似拡大して遠方へ（角度サイズ保存＝構図不変・書き割り感の解消）
    /// ②eye カメラの clear を SolidColor 暗グレーに（写真の外側の青空 skybox を暗転）。
    ///
    /// Harmony は使わずポーリング（SetBG は async UniTask で Postfix が await 完了前に走るため不適。
    /// ポーリングは rig 再構築・シーン遷移へ構造的に追従する＝ProjectorRunner と同パターン）。
    /// 適用条件が崩れたら即復元するベストエフォート設計。
    /// m_bg は private だが Assembly-CSharp は Publicize="true" 参照のため直接読める。
    /// </summary>
    internal sealed class Talk2DBackdropRunner : MonoBehaviour
    {
        // 押し出し適用中の m_bg（null=未適用）。元値と基準点 P は初回 capture を使い続ける
        //（毎フレ再取得だとカメラが動く演出で push 結果がジッタするため。spec §4-3）。
        private Transform m_pushedBg;
        private Vector3 m_origLocalPos;
        private Vector3 m_origLocalScale;
        private Vector3 m_camLocalPos;
        private bool m_appliedLogged;

        private void Update()
        {
            // 適用条件は共有 Gate へ集約（dim 暗転は EyeCullingCoordinator 管轄＝eye 書き込みはしない）。
            if (!Talk2DBackdropGate.IsActive(out Talk2DScene t2d, out GameObject bgGo))
            {
                RestoreBg();
                return;
            }

            Transform rig = VRModCore.GetRigTransform();
            List<Camera> eyes = CollectEyeCameras(rig);
            if (eyes.Count == 0) return; // 遷移等の過渡。適用も復元もしない（次フレーム再判定）

            // --- ①遠方押し出し ---
            if (m_pushedBg != bgGo.transform)
            {
                RestoreBg(); // 別 instance へ切替（Afternoon→Evening 等）。旧側を戻してから新側を capture
                Camera sceneCam = FindSceneCamera(t2d, rig);
                if (sceneCam == null) return; // シーンカメラ未確定の過渡
                m_pushedBg = bgGo.transform;
                m_origLocalPos = m_pushedBg.localPosition;
                m_origLocalScale = m_pushedBg.localScale;
                // 基準点 P を m_bg.parent の local 空間へ変換（親に scale があっても押し出しが成立する）。
                m_camLocalPos = m_pushedBg.parent != null
                    ? m_pushedBg.parent.InverseTransformPoint(sceneCam.transform.position)
                    : sceneCam.transform.position;
                m_appliedLogged = false;
            }

            // 毎フレーム「保存した元値 × k_eff」で書く＝累積適用が構造的に起きない + 倍率スライダーの live 反映。
            float far = eyes[0].farClipPlane;
            var push = BackdropSolver.Push(m_origLocalPos, m_origLocalScale, m_camLocalPos,
                Configs.Talk2DBackdropDistanceMul.Value, far);
            m_pushedBg.localPosition = push.LocalPosition;
            m_pushedBg.localScale = push.LocalScale;
            if (!m_appliedLogged)
            {
                Plugin.Log.LogInfo($"[Talk2DBackdrop] 背景押し出し適用 (k={push.EffectiveMul:F1}, eyeFar={far:F0})");
                m_appliedLogged = true;
            }
        }

        // eye カメラ = rig 配下の enabled=false（fork が手動 Render する）Camera。
        // targetTexture は同定に使えない: fork は RenderEye の瞬間だけ RT を割り当てるため
        // ポーリングから見ると常時 null（実測 2026-06-05。当初の targetTexture!=null フィルタは
        // 常に 0 件＝機能全体が不発になる実バグだった）。
        private readonly List<Camera> m_eyeBuf = new List<Camera>();
        private List<Camera> CollectEyeCameras(Transform rig)
        {
            m_eyeBuf.Clear();
            if (rig == null) return m_eyeBuf;
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                if (!cam.enabled) m_eyeBuf.Add(cam);
            }
            return m_eyeBuf;
        }

        // 基準点 P 用のシーンカメラ。rig 配下（eye）は除外する（fork は rig をゲームカメラへ
        // 追従させるため、階層上 rig がカメラ配下に入る構成があり得る）。
        private static Camera FindSceneCamera(Talk2DScene t2d, Transform rig)
        {
            foreach (var cam in t2d.GetComponentsInChildren<Camera>(true))
            {
                if (rig == null || !cam.transform.IsChildOf(rig)) return cam;
            }
            return null;
        }

        private void RestoreBg()
        {
            // Unity fake-null: scene unload で道連れ破棄済みなら skip（abort しないベストエフォート）。
            if (m_pushedBg != null)
            {
                m_pushedBg.localPosition = m_origLocalPos;
                m_pushedBg.localScale = m_origLocalScale;
            }
            m_pushedBg = null;
        }

        private void RestoreAll()
        {
            bool any = m_pushedBg != null;
            RestoreBg();
            if (any) Plugin.Log.LogInfo("[Talk2DBackdrop] 復元");
        }

        private void OnDestroy() => RestoreAll();
    }
}
