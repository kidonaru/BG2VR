using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.Locomotion
{
    /// <summary>
    /// grip locomotion（掴み移動 + スティック平行移動）の毎フレ統括。ProjectorRunner の定常状態から Tick され、rig transform を直接書き換える
    /// （rig は SetupCameraRig 後は静的アンカー＝外部から動かしても fork と干渉しない・spec §2）。
    /// rig 参照の変化（遷移 teardown→再構築）を自己検出して状態と累積をクリアする。
    /// 差分は tracking 空間（snapshot の rig-local pose）で取るため、rig がどう動いても marker は
    /// ズレない（world 空間で marker を持つ方式の再同期問題が構造的に消える・spec §1）。
    /// </summary>
    internal sealed class GrabMoveRunner
    {
        /// <summary>ProjectorRunner が arbiter 除外・ポインタ凍結に使う grip 関与状態。</summary>
        public struct GripBusy
        {
            public bool Left;
            public bool Right;
        }

        private readonly GrabMoveState m_state = new GrabMoveState();
        private readonly GripPoseSmoother m_smoother = new GripPoseSmoother();
        private Transform m_boundRig;
        private GripHand m_markerHand = GripHand.None;
        private Vector3 m_prevLocalPos;
        private Quaternion m_prevLocalRot;
        // 累積変換 A（リセット = A⁻¹ 適用。初期 pose 保存にしない理由は GrabMoveSolver.AccumulateDelta 参照）。
        private Vector3 m_accumPos = Vector3.zero;
        private Quaternion m_accumRot = new Quaternion(0f, 0f, 0f, 1f);

        public GripBusy Tick(Transform rig, VrControllerSnapshot left, VrControllerSnapshot right,
            Vector3 headForwardHoriz, Vector3 headRightHoriz, float dt)
        {
            // 遷移/再構築で rig が差し替わった → 状態・累積を破棄して bind し直す。
            if (!ReferenceEquals(rig, m_boundRig))
            {
                Reset();
                m_boundRig = rig;
            }

            bool grab = global::BG2VR.Configs.EnableGrabMove.Value;
            bool stick = global::BG2VR.Configs.EnableStickMove.Value;
            if (!grab && !stick)
            {
                // 両 locomotion OFF: 累積は意図的に保持（OFF 中の外部 rig 変更は目線高さ＝yaw と可換で
                // A⁻¹ の正確性は保たれ、再 ON 後の両手リセットで OFF 前移動分も巻き戻せる）。
                m_state.Clear();
                m_markerHand = GripHand.None;
                return default;
            }

            var r = m_state.Update(left.Valid, left.Grip, right.Valid, right.Grip, dt);

            if (r.ResetNow)
            {
                // 両手 grip 1 秒: locomotion の累積（掴み + スティック）だけを巻き戻す（目線高さ等は保存）。
                var restored = GrabMoveSolver.ApplyInverse(m_accumPos, m_accumRot, rig.position, rig.rotation);
                rig.SetPositionAndRotation(restored.Position, restored.Rotation);
                m_accumPos = Vector3.zero;
                m_accumRot = new Quaternion(0f, 0f, 0f, 1f);
                m_markerHand = GripHand.None;
            }
            else if (r.MoveHand != GripHand.None)
            {
                VrControllerSnapshot snap = (r.MoveHand == GripHand.Left) ? left : right;
                bool seed = r.MarkerResync || m_markerHand != r.MoveHand;

                // ① 掴み移動（grab-and-drag）: tracking 空間 pose を EMA → solver。seed フレームは移動せず marker 取り直し。
                if (grab)
                {
                    if (seed) m_smoother.Reset();
                    m_smoother.Update(
                        snap.RigLocalPosition, snap.RigLocalRotation,
                        dt, global::BG2VR.Configs.GrabMoveSmoothingTau.Value,
                        out Vector3 smPos, out Quaternion smRot);
                    if (!seed)
                    {
                        Vector3 oldPos = rig.position;
                        Quaternion oldRot = rig.rotation;
                        var pose = GrabMoveSolver.Step(
                            oldPos, oldRot, rig.localScale.x,
                            m_prevLocalPos, m_prevLocalRot,
                            smPos, smRot);
                        rig.SetPositionAndRotation(pose.Position, pose.Rotation);
                        GrabMoveSolver.AccumulateDelta(
                            oldPos, oldRot, pose.Position, pose.Rotation,
                            ref m_accumPos, ref m_accumRot);
                    }
                    m_prevLocalPos = smPos;
                    m_prevLocalRot = smRot;
                }

                // ② スティック平行移動（頭基準・水平）: 移動の手の raw stick を使う（速度ベース・marker 不要＝
                //    seed フレームでも適用してよい）。両手 grip（DualHold）は MoveHand=None でここに来ず＝凍結。
                if (stick)
                {
                    Vector3 d = StickMoveSolver.ComputeDelta(
                        snap.Stick, headForwardHoriz, headRightHoriz,
                        global::BG2VR.Configs.StickMoveSpeed.Value,
                        global::BG2VR.Configs.StickMoveDeadzone.Value, dt);
                    if (d != Vector3.zero)
                    {
                        Vector3 oldPos = rig.position;
                        Quaternion oldRot = rig.rotation;
                        rig.position = oldPos + d;
                        GrabMoveSolver.AccumulateDelta(
                            oldPos, oldRot, rig.position, rig.rotation,
                            ref m_accumPos, ref m_accumRot);
                    }
                }

                // grab ON のときのみ marker を持つ（OFF→ON live 切替で seed させ jump を防ぐ）。
                m_markerHand = grab ? r.MoveHand : GripHand.None;
            }
            else
            {
                m_markerHand = GripHand.None; // Idle / DualHold（移動凍結）/ ResetWait
            }

            return new GripBusy { Left = r.LeftBusy, Right = r.RightBusy };
        }

        public void Reset()
        {
            m_state.Clear();
            m_smoother.Reset();
            m_boundRig = null;
            m_markerHand = GripHand.None;
            m_accumPos = Vector3.zero;
            m_accumRot = new Quaternion(0f, 0f, 0f, 1f);
        }
    }
}
