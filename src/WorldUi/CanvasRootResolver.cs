using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// 投影対象 root canvas（#5 GBSystem(Clone)/Canvas + 現シーン Canvas）を解決する。
    /// nested(親に Canvas) は除外（親経由で同梱）。ツール(UnityExplorer/UniverseLib)も除外。
    /// </summary>
    internal static class CanvasRootResolver
    {
        public static List<Canvas> Resolve()
        {
            var result = new List<Canvas>();
            var all = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (var c in all)
            {
                if (c == null) continue;

                bool isLive = c.gameObject.scene.IsValid();           // prefab は scene 無効
                bool isActive = c.isActiveAndEnabled;
                bool isOverlay = c.renderMode == RenderMode.ScreenSpaceOverlay;
                bool hasRaycaster = c.GetComponent<GraphicRaycaster>() != null;

                if (!CanvasRootClassifier.IsProjectableRoot(isLive, isActive, isOverlay, hasRaycaster))
                    continue;
                if (IsNested(c)) continue;                            // 親に Canvas → nested
                if (IsToolCanvas(c)) continue;                        // UnityExplorer/UniverseLib

                result.Add(c);
            }
            return result;
        }

        private static bool IsNested(Canvas c)
        {
            Transform p = c.transform.parent;
            while (p != null)
            {
                if (p.GetComponent<Canvas>() != null) return true;
                p = p.parent;
            }
            return false;
        }

        private static bool IsToolCanvas(Canvas c)
        {
            string root = c.transform.root != null ? c.transform.root.name : c.gameObject.name;
            return root.StartsWith("com.sinai") || c.gameObject.name.StartsWith("com.sinai");
        }
    }
}
