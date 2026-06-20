using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// world ray と quad 平面の交差を解き、ヒット点の RT pixel 座標（原点=左下）を返す純関数。
    /// halfWidth/halfHeight は呼び出し側が lossyScale 乗算済みの「実ワールド寸法」を渡す契約。
    /// Transform を受けず基底ベクトル（right/up/normal）で受ける（Quaternion ECall 回避・テスト可能）。
    /// u/v の水平/垂直の向きは quad facing/handedness 依存＝実機で要確認（逆なら runner 側で対処）。
    /// </summary>
    public static class QuadRaycaster
    {
        public struct Hit
        {
            public bool Valid;
            public Vector2 Pixel;      // RT pixel（原点=左下、GraphicRaycaster 規約と一致）
            public Vector3 WorldPoint; // 交点（world）
            public Vector3 Normal;     // ヒット面の法線（world・単位・ray 始点側を向く）
        }

        public static Hit Raycast(
            Vector3 rayOrigin, Vector3 rayDir,
            Vector3 quadCenter, Vector3 quadRight, Vector3 quadUp, Vector3 quadNormal,
            float halfWidth, float halfHeight, int rtWidth, int rtHeight)
        {
            float denom = Vector3.Dot(rayDir, quadNormal);
            if (Mathf.Abs(denom) < 1e-6f) return default;          // 平面に平行
            float t = Vector3.Dot(quadCenter - rayOrigin, quadNormal) / denom;
            if (t < 0f) return default;                            // コントローラ後方
            Vector3 hit = rayOrigin + rayDir * t;
            Vector3 rel = hit - quadCenter;
            float x = Vector3.Dot(rel, quadRight);
            float y = Vector3.Dot(rel, quadUp);
            if (Mathf.Abs(x) > halfWidth || Mathf.Abs(y) > halfHeight) return default; // 矩形外
            float u = x / (2f * halfWidth) + 0.5f;
            float v = y / (2f * halfHeight) + 0.5f;
            return new Hit
            {
                Valid = true,
                Pixel = new Vector2(u * rtWidth, v * rtHeight),
                WorldPoint = hit,
                // 視点側法線: denom=dot(rayDir, quadNormal) の符号で quadNormal を ray 始点側へ向ける
                //（符号不感設計の維持・呼び出し側は forward/-forward どちらを渡しても良い）。
                Normal = denom > 0f ? -quadNormal : quadNormal,
            };
        }
    }
}
