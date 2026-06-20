using GB.Bar.MiniGame;
using UnityEngine;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using BG2VR.ScenePinned;

namespace BG2VR.CameraFollow
{
    /// <summary>
    /// ゲームカメラの「位置」変化を毎フレーム rig へ差分転写する（回転は一切追従しない。spec §3）。
    /// rig の絶対位置を所有しない差分合成なので、GrabMove のユーザー offset・再センターと自然に共存する。
    /// rig 差替え（遷移 teardown→再構築）/ カメラ GO 変化を ReferenceEquals で自己検出して
    /// baseline を破棄し、fork の SetupCameraRig 再スナップと二重適用しない（GrabMoveRunner と同型）。
    /// eye render は fork manager の Update 内のため実行順次第で 1 フレーム遅れるが、
    /// 位置のみの連続追従では知覚不能として許容（spec §3.3）。
    /// </summary>
    internal sealed class CameraPositionFollowRunner : MonoBehaviour
    {
        private readonly CameraFollowState m_state = new CameraFollowState();
        private Transform m_boundRig;
        private Camera m_boundCamera;

        private void Update()
        {
            if (!global::BG2VR.Configs.FollowCameraPosition.Value || !VRModCore.IsVrActive || IsKaraokeActive()
                || ScenePinnedPoseState.IsCurrentEnvPinned)
            {
                // OFF / VR 非稼働 / カラオケ中 / 当該 env が固定位置 pinned のとき追従しない
                // （pinned 時は ScenePinnedPoseRunner が rig pose を所有。spec §4.5）
                Unbind();
                return;
            }

            Transform rig = VRModCore.GetRigTransform();
            Camera cam = CameraFinder.FindGameCamera();
            if (rig == null || cam == null)
            {
                Unbind(); // 遷移 teardown 中（rig 不在）/ カメラ未解決
                return;
            }

            if (!ReferenceEquals(rig, m_boundRig) || !ReferenceEquals(cam, m_boundCamera))
            {
                // 新 rig は fork がカメラ位置へ再スナップ済み＝差分を持ち越さない
                m_state.Invalidate();
                m_boundRig = rig;
                m_boundCamera = cam;
            }

            Vector3 delta = m_state.Step(cam.transform.position);
            // ガードは静止フレームで無駄な Transform write を避ける最適化（近似比較）。
            // rebind 初回の一括ジャンプ防止は CameraFollowState.Step の baseline 側（厳密ゼロ）の責務。
            if (delta != Vector3.zero) rig.position += delta;
        }

        /// <summary>
        /// カラオケ進行中か。カラオケは Cinemachine + Timeline 駆動の演出カメラが大きく動き回るため
        /// 位置追従を停止する（追従すると酔う・演出破綻）。終了時は Unbind 済み baseline が
        /// re-baseline されるためジャンプしない。s_instance は Setup で set / Release で null（publicize 参照）。
        /// </summary>
        private static bool IsKaraokeActive() => MiniGameBase.s_instance is Karaoke;

        private void Unbind()
        {
            m_state.Invalidate();
            m_boundRig = null;
            m_boundCamera = null;
        }
    }
}
