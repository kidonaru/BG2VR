using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BG2VR.Patches
{
    /// <summary>
    /// FixMod の高解像度チェキ holder（<c>BunnyGarden2FixMod.Patches.ChekiHiResSidecar</c>）への reflection ブリッジ。
    /// FixMod へのコンパイル参照なし（<see cref="AccessTools.TypeByName"/> で解決・不在なら全 API no-op＝FixMod 未導入で正常）。
    /// VRチェキが撮った hi-res を sidecar へ上書きし、FixMod の ExSave 保存経路（GameData.SaveCheki Postfix）に
    /// VR 画像を流す。既存パターン: <see cref="FreeCameraVrGuard"/> と同型（lazy 解決・不在 no-op・一度だけログ）。
    /// </summary>
    internal static class FixModChekiHiResBridge
    {
        private static bool s_resolved;
        private static bool s_available;
        private static MethodInfo s_isFresh;     // static bool IsFresh()
        private static FieldInfo s_capturedSize; // static int CapturedSize
        private static MethodInfo s_store;       // static void Store(Texture2D, int)

        private static void EnsureResolved()
        {
            if (s_resolved) return;
            s_resolved = true;

            var type = AccessTools.TypeByName("BunnyGarden2FixMod.Patches.ChekiHiResSidecar");
            if (type == null)
            {
                Plugin.Log.LogInfo("[ChekiHiRes] FixMod ChekiHiResSidecar 不在のため hi-res 連携無効（FixMod 未導入なら正常）。");
                return;
            }
            s_isFresh = AccessTools.Method(type, "IsFresh");
            s_capturedSize = AccessTools.Field(type, "CapturedSize");
            s_store = AccessTools.Method(type, "Store", new[] { typeof(Texture2D), typeof(int) });
            if (s_isFresh == null || s_capturedSize == null || s_store == null)
            {
                Plugin.Log.LogWarning("[ChekiHiRes] ChekiHiResSidecar のメンバー解決に失敗。hi-res 連携無効。");
                return;
            }
            s_available = true;
            Plugin.Log.LogInfo("[ChekiHiRes] FixMod 高解像度チェキ連携を有効化。");
        }

        /// <summary>FixMod hi-res がこのショットで走り、sidecar に fresh な hi-res tex がある（＝上書き対象あり）。</summary>
        public static bool HasFreshHiRes()
        {
            EnsureResolved();
            if (!s_available) return false;
            return (bool)s_isFresh.Invoke(null, null);
        }

        /// <summary>FixMod が意図した hi-res 一辺（= ChekiSize）。未解決時は 0。</summary>
        public static int TargetSize()
        {
            EnsureResolved();
            if (!s_available) return 0;
            return (int)s_capturedSize.GetValue(null);
        }

        /// <summary>
        /// VR 由来 hi-res tex で sidecar を上書きする。FixMod の <c>Store</c> は先頭で既存 tex を破棄し、
        /// 渡した tex の所有権を取る（CapturedFrame 再スタンプで IsFresh を維持）＝呼び出し側は破棄しない。
        /// </summary>
        public static void Store(Texture2D tex, int size)
        {
            EnsureResolved();
            if (!s_available) return;
            s_store.Invoke(null, new object[] { tex, size });
        }
    }
}
