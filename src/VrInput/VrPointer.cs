using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrInput
{
    /// <summary>
    /// コントローラ snapshot を rig 子 GO に適用し world ray（origin/direction）を提供する。
    /// rig の子＝worldScale 自動継承・遷移 teardown で道連れ破棄。
    /// </summary>
    internal sealed class VrPointer
    {
        private GameObject m_go;

        public bool Exists => m_go != null;
        public Transform Transform => m_go != null ? m_go.transform : null;
        public Vector3 Origin => m_go != null ? m_go.transform.position : Vector3.zero;
        // aim 軸: 実機でレーザーが逆向きなら -m_go.transform.forward に変更（spec §10）。
        public Vector3 Direction => m_go != null ? m_go.transform.forward : Vector3.forward;

        public void Create(Transform rig)
        {
            m_go = new GameObject("BG2VR_Pointer");
            m_go.hideFlags = HideFlags.HideAndDontSave;
            m_go.transform.SetParent(rig, false);
        }

        // pitchDeg: コントローラ local +X 軸まわりの下向きピッチ（度・正=下向き）。生デバイス pose は
        // 自然把持でレーザーがやや上向きに感じられるため、下向きへ倒す（Configs.VrLaserPitchDeg）。
        // pointer transform 自体を傾けるので、レーザー可視線・レティクル・QuadRaycast（いずれも forward 派生）が
        // 一貫して追従する。
        public void Apply(VrControllerSnapshot snap, float pitchDeg)
        {
            if (m_go == null) return;
            m_go.transform.localPosition = snap.RigLocalPosition;
            m_go.transform.localRotation = snap.RigLocalRotation * Quaternion.AngleAxis(pitchDeg, Vector3.right);
        }

        public void Destroy()
        {
            if (m_go != null) Object.Destroy(m_go);
            m_go = null;
        }
    }
}
