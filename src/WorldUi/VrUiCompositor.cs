using System.Collections.Generic;
using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// UI ortho カメラ + RenderTexture を所有し、対象 canvas を 1 枚の RT に合成する。
    /// draw order は各 canvas の sortingOrder。planeDistance は ortho frustum 内に収めるだけ。
    /// </summary>
    internal sealed class VrUiCompositor
    {
        public const int RtWidth = 1920;
        public const int RtHeight = 1080;
        private const float PlaneDistance = 1f;

        private GameObject m_camGo;
        private Camera m_uiCamera;
        private RenderTexture m_rt;
        private readonly List<ManagedCanvas> m_managed = new List<ManagedCanvas>();

        public RenderTexture Texture => m_rt;
        public Camera UiCamera => m_uiCamera;

        /// <param name="cullingMask">解決 canvas レイヤの和集合（Default 除外済み）。</param>
        public void Create(int cullingMask)
        {
            m_rt = new RenderTexture(RtWidth, RtHeight, 24, RenderTextureFormat.ARGB32);
            m_rt.Create();

            m_camGo = new GameObject("BG2VR_UiCamera");
            m_camGo.hideFlags = HideFlags.HideAndDontSave;
            // ScreenSpaceCamera 化した canvas plane を world z=0 に乗せる（カメラ forward=+z・planeDistance=1）。
            // ゲームの InstantiateAsync(worldPositionStays=true) で動的生成される UI 子が canvas world z
            // 由来の不正 local z を焼き込まれ不可視化するのを防ぐ（Pixi/SNS 空表示の真因・実測 2026-06-20）。
            m_camGo.transform.position = new Vector3(0f, 0f, -PlaneDistance);
            Object.DontDestroyOnLoad(m_camGo);

            m_uiCamera = m_camGo.AddComponent<Camera>();
            m_uiCamera.orthographic = true;
            m_uiCamera.clearFlags = CameraClearFlags.SolidColor;
            m_uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透明背景（UI 以外は抜く）
            m_uiCamera.cullingMask = cullingMask;
            m_uiCamera.targetTexture = m_rt;
            m_uiCamera.depth = -100f;        // eye render より前に RT 更新を狙う（手動 Render フォールバックは未実装＝実機で遅延を観測したら追加検討）
            m_uiCamera.nearClipPlane = 0.01f;
            m_uiCamera.farClipPlane = 10f;

            // FirstScene.Start 等「全カメラに UACD がある」前提のループでの NRE を防ぐ（fork eye カメラと同理由）。
            var uacdType = HarmonyLib.AccessTools.TypeByName("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (uacdType != null && m_camGo.GetComponent(uacdType) == null) m_camGo.AddComponent(uacdType);
        }

        /// <summary>対象 canvas を ScreenSpaceCamera 化（この UI カメラへ）。</summary>
        public void Attach(IReadOnlyList<Canvas> canvases)
        {
            foreach (var c in canvases)
            {
                if (c == null) continue;
                var mc = new ManagedCanvas(c);
                mc.Convert(m_uiCamera, PlaneDistance);
                m_managed.Add(mc);
            }
        }

        public void RestoreAll()
        {
            foreach (var mc in m_managed) mc.Restore();
            m_managed.Clear();
        }

        public void Destroy()
        {
            RestoreAll();
            if (m_camGo != null) Object.Destroy(m_camGo);
            m_camGo = null;
            m_uiCamera = null;
            if (m_rt != null) { m_rt.Release(); Object.Destroy(m_rt); m_rt = null; }
        }
    }
}
