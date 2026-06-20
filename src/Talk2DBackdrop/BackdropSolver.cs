using UnityEngine;

namespace BG2VR.Talk2DBackdrop
{
    /// <summary>
    /// Talk2DScene の背景 quad をカメラ基準で相似拡大（遠方押し出し）する純関数。
    /// 角度サイズ保存＝構図不変のまま両眼視差をほぼ 0 にし、書き割り感を消す（spec §4-3）。
    /// 入出力は Vector3 に閉じる（PlacementSolver と同方針。Quaternion 系 native ECall は
    /// テストホストに存在せず例外になるため）。
    /// </summary>
    public static class BackdropSolver
    {
        // eye far clip に対する安全率。押し出し後の距離はこれを超えない（far 面クリップで背景消失を防ぐ）。
        public const float FarSafetyRatio = 0.85f;

        public struct PushResult
        {
            public Vector3 LocalPosition;
            public Vector3 LocalScale;
            public float EffectiveMul; // clamp 後に実際へ適用された倍率
        }

        /// <summary>
        /// mul を [1, eyeFar*FarSafetyRatio/origDist] に clamp した実効倍率。
        /// 下限 1＝「元より近づける」退行を構造的に排除（far が極端に小さい環境でも no-op 留まり）。
        /// </summary>
        public static float ComputeEffectiveMul(float mul, float origDist, float eyeFar)
        {
            if (origDist <= 1e-4f) return 1f; // カメラと同位置＝押す方向が定まらない退避
            float kMax = eyeFar * FarSafetyRatio / origDist;
            return Mathf.Max(1f, Mathf.Min(mul, kMax));
        }

        /// <summary>
        /// camLocalPos（基準点 P）から origPos へ向かう半直線上で相似拡大する。
        /// 全引数は m_bg.parent の local 空間で渡すこと（同一空間なら成立。world でも可）。
        /// 常に「元値 × 倍率」で計算するため、毎フレーム再適用しても累積しない（冪等）。
        /// </summary>
        public static PushResult Push(Vector3 origPos, Vector3 origScale, Vector3 camLocalPos, float mul, float eyeFar)
        {
            float origDist = (origPos - camLocalPos).magnitude;
            float k = ComputeEffectiveMul(mul, origDist, eyeFar);
            return new PushResult
            {
                LocalPosition = camLocalPos + (origPos - camLocalPos) * k,
                LocalScale = origScale * k,
                EffectiveMul = k,
            };
        }
    }
}
