using System.Collections.Generic;

namespace BG2VR.CameraBridge
{
    /// <summary>
    /// カメラ選定フィルタ用の候補メタデータ（UnityEngine.Camera から抽出した純値）。
    /// </summary>
    public readonly struct CameraCandidate
    {
        public readonly int Index;           // 元コレクションでの添字（最終的に呼び出し側が Camera を引く）
        public readonly bool ActiveAndEnabled;
        public readonly bool HasTargetTexture; // RT 描画（VR eye / post-FX）は対象外
        public readonly int CullingMask;
        public readonly float Depth;
        public readonly bool NameExcluded;     // 既知の VR rig カメラ名など、明示除外

        public CameraCandidate(int index, bool activeAndEnabled, bool hasTargetTexture, int cullingMask, float depth, bool nameExcluded)
        {
            Index = index;
            ActiveAndEnabled = activeAndEnabled;
            HasTargetTexture = hasTargetTexture;
            CullingMask = cullingMask;
            Depth = depth;
            NameExcluded = nameExcluded;
        }
    }

    /// <summary>
    /// 「VR rig を乗せるべきゲームの 3D カメラ」を選ぶ純関数（plan §3①）。
    /// UnityEngine 非依存にして xUnit でテスト可能にする。
    ///
    /// フィルタ: ActiveAndEnabled && スクリーン描画(TargetTexture 無し) && 非除外名 &&
    ///          cullingMask が UI 以外のレイヤを含む（= 3D を描く）。
    /// 残った候補のうち depth 最大を採用（同値は添字が小さい方）。
    /// </summary>
    public static class CameraSelector
    {
        // Unity 既定の "UI" レイヤ（layer 5）。これ以外のビットを持てば 3D を描くとみなす。
        public const int UILayer = 5;
        public const int UILayerMask = 1 << UILayer;

        /// <summary>採用すべき候補の <see cref="CameraCandidate.Index"/> を返す。該当無しは -1。</summary>
        public static int SelectBestIndex(IReadOnlyList<CameraCandidate> candidates)
        {
            int bestIndex = -1;
            float bestDepth = 0f;
            bool found = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.ActiveAndEnabled) continue;
                if (c.HasTargetTexture) continue;
                if (c.NameExcluded) continue;
                // UI 専用カメラ（cullingMask が UI ビットのみ）は除外する。
                if ((c.CullingMask & ~UILayerMask) == 0) continue;

                if (!found || c.Depth > bestDepth)
                {
                    found = true;
                    bestDepth = c.Depth;
                    bestIndex = c.Index;
                }
            }

            return bestIndex;
        }
    }
}
