using UnityEngine;

namespace BG2VR.CameraFollow
{
    /// <summary>
    /// ゲームカメラ位置追従の baseline 管理（純関数・xUnit 対象。spec §3.2）。
    /// Step は前回位置との world 差分を返し baseline を更新する。
    /// Invalidate 後の初回 Step は差分ゼロで baseline を再設定する
    /// （fork の rig 再スナップとの二重適用・OFF 中の移動分の一括ジャンプを防ぐ）。
    /// </summary>
    internal sealed class CameraFollowState
    {
        private bool m_hasBaseline;
        private Vector3 m_lastCamPos;

        public Vector3 Step(Vector3 camPos)
        {
            Vector3 delta = m_hasBaseline ? camPos - m_lastCamPos : Vector3.zero;
            m_lastCamPos = camPos;
            m_hasBaseline = true;
            return delta;
        }

        public void Invalidate() => m_hasBaseline = false;
    }
}
