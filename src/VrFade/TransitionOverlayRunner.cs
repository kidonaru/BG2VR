using System;
using GB;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrFade
{
    /// <summary>
    /// ゲームの遷移絵柄(ScreenFade.m_transition)を VR compositor overlay へ毎フレミラーする常駐 runner。
    /// rig 非依存（BG2VR_Runtime 上）＝遷移 rig teardown 中も compositor が overlay を描画し続ける
    /// （VR セッション維持が前提・spec §3.2）。
    /// 白 compositor fade（VrFadeRunner）は無変更で併存＝FOV 端の埋め + フォールバック（spec §3.4）。
    /// </summary>
    internal sealed class TransitionOverlayRunner : MonoBehaviour
    {
        // 直近で push に成功した状態。push 失敗（session 非生存）時は更新しない＝
        // session 復帰後に差分検知で自動再 push される（VrFadeRunner と同じ規約）。
        private bool m_lastVisible;
        private float m_lastAlpha;
        private float m_lastWidth = -1f;
        private float m_lastDistance = -1f;
        // 直近で push に成功した source texture と rect。変化時のみ blit + texture 再 push する。
        private Texture2D m_lastSrcTex;
        private Rect m_lastTexRect;
        // sprite texture の blit 先。ゲームの遷移絵柄は DXT1 圧縮アセットで、SetOverlayTexture が
        // InvalidTexture で拒否するため（実測 2026-06-07）、ARGB32 RT へ GPU コピーしてから渡す。
        private RenderTexture m_blitRt;

        private void Update()
        {
            // featureEnabled=false 経路が hide エッジを 1 回 push して以降何もしない。
            bool featureEnabled = global::BG2VR.Configs.EnableVrTransitionOverlay.Value;
            ScreenFade fade = GBSystem.Instance != null ? GBSystem.Instance.m_fade : null;
            UnityEngine.UI.Image transition = fade != null ? fade.m_transition : null;
            // activeInHierarchy: ScreenFade root ごと非 active のケースも拾う（VrFadeRunner と同方針）。
            bool active = transition != null && transition.gameObject.activeInHierarchy;
            float alpha = active ? transition.color.a : 0f;
            float width = global::BG2VR.Configs.VrTransitionOverlayWidth.Value;
            float distance = global::BG2VR.Configs.VrTransitionOverlayDistance.Value;

            var d = TransitionOverlayPolicy.Decide(featureEnabled, active, alpha, width, distance,
                m_lastVisible, m_lastAlpha, m_lastWidth, m_lastDistance);

            // 表示 push の前に texture を確定させる。取れない間は表示しない（白 fade フォールバックに任せる）。
            if (d.ShouldPush && d.Visible && !EnsureTexturePushed(transition))
            {
                return;
            }

            Push(d, width, distance);
        }

        // overlay 残留＝視界に絵柄が貼り付くのを防ぐ（VrFadeRunner.ClearIfPushed と同型）。
        private void OnDestroy()
        {
            if (m_lastVisible && VRModCore.SetTransitionOverlayState(false, 0f, m_lastWidth, m_lastDistance))
            {
                m_lastVisible = false;
                m_lastAlpha = 0f;
            }
            ReleaseBlitRt();
        }

        // sprite texture を所有 RT に blit し、RT の native ptr + atlas UV を解決して
        // 前回 push と異なる時のみ再 push する（blit は GPU コピー＝非 readable/圧縮アセットでも可）。
        private bool EnsureTexturePushed(UnityEngine.UI.Image transition)
        {
            Sprite sprite = transition != null ? transition.sprite : null;
            Texture2D tex = sprite != null ? sprite.texture : null;
            if (tex == null) return false;
            Rect rect = sprite.textureRect;
            if (tex == m_lastSrcTex && rect == m_lastTexRect) return true;
            // RT は source texture と同サイズで確保（サイズ違いの texture に差し替わったら作り直し）
            if (m_blitRt == null || m_blitRt.width != tex.width || m_blitRt.height != tex.height)
            {
                ReleaseBlitRt();
                m_blitRt = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                m_blitRt.Create();
            }
            Graphics.Blit(tex, m_blitRt);
            IntPtr ptr = m_blitRt.GetNativeTexturePtr();
            if (ptr == IntPtr.Zero) return false;
            var uv = OverlayUvMapper.Map(rect, tex.width, tex.height, flipV: true);
            if (!VRModCore.SetTransitionOverlayTexture(ptr, m_blitRt.width, m_blitRt.height, uv.UMin, uv.VMin, uv.UMax, uv.VMax)) return false;
            m_lastSrcTex = tex;
            m_lastTexRect = rect;
            return true;
        }

        private void ReleaseBlitRt()
        {
            if (m_blitRt == null) return;
            m_blitRt.Release();
            UnityEngine.Object.Destroy(m_blitRt);
            m_blitRt = null;
            m_lastSrcTex = null; // RT を失ったら texture push 済み状態も無効化（次の show で再 blit）
        }

        private void Push(TransitionOverlayPolicy.Decision d, float width, float distance)
        {
            if (!d.ShouldPush) return;
            if (VRModCore.SetTransitionOverlayState(d.Visible, d.Alpha, width, distance))
            {
                m_lastVisible = d.Visible;
                m_lastAlpha = d.Alpha;
                m_lastWidth = width;
                m_lastDistance = distance;
            }
        }
    }
}
