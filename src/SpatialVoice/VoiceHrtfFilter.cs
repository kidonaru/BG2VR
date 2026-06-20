using UnityEngine;
using BG2VR.SpatialVoice.SteamAudio;
using static BG2VR.SpatialVoice.SteamAudio.PhononNative;

namespace BG2VR.SpatialVoice
{
    /// <summary>
    /// ミラー voice GO 専用の HRTF フィルタ。OnAudioFilterRead で最新方向（volatile）を読み
    /// <see cref="BinauralRenderer.Process"/> で interleaved stereo を上書きする。
    /// active=false / renderer 未 ready ならバイパス（素通し）。
    /// **専用 GO 不変条件**: この GO は AudioSource を 1 個だけ持ち、AudioListener や他 source を
    /// 絶対に同居させない（OnAudioFilterRead は同 GO の全 source 合算を受け取るため）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    internal sealed class VoiceHrtfFilter : MonoBehaviour
    {
        private BinauralRenderer m_renderer;

        // main → audio の一方向 volatile 受け渡し（lock-free・最新値勝ち・1〜数フレーム遅延は許容）
        private volatile float m_dirX;
        private volatile float m_dirY;
        private volatile float m_dirZ = -1f; // 既定 = 正面（Steam Audio −Z）
        private volatile float m_blend = 1f;
        private volatile float m_gain = 1f;
        private volatile bool m_active;

        public void Bind(BinauralRenderer renderer) => m_renderer = renderer;

        /// <summary>main スレッドから毎フレ更新。dir は Steam Audio 座標の単位ベクトル。</summary>
        public void SetParams(Vector3 dir, float spatialBlend, float gain, bool active)
        {
            m_dirX = dir.x;
            m_dirY = dir.y;
            m_dirZ = dir.z;
            m_blend = spatialBlend;
            m_gain = gain;
            m_active = active;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            BinauralRenderer r = m_renderer;
            if (!m_active || r == null || !r.Ready) return; // バイパス
            try
            {
                r.Process(data, channels, new IPLVector3(m_dirX, m_dirY, m_dirZ), m_blend, m_gain);
            }
            catch
            {
                // audio スレッドでは例外を伝播させない（このブロックは素通し）
            }
        }
    }
}
