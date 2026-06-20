using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>
    /// world ray と縦軸円筒弧パネル（凹面=視点側・PanelMeshBuilder と同パラメータ）の交差を解き
    /// RT pixel 座標を返す純関数。world→panel-local 化は共役 quaternion（q*q / q*v 演算子は
    /// managed＝xUnit 可・PanelGrabSolver と同パターン）。
    /// 二次方程式の大きい方の根 t₂ を採用する＝+z 側アーク（凹面）との交点。視点が円筒の内側
    /// （R > 距離）でも外側でも、パネル面は常に exit 側になる（spec §4）。背面からの命中は
    /// 非対応（ポインタは常に視点側で使う前提・spec §2）。
    /// </summary>
    public static class CurvedPanelRaycaster
    {
        public static QuadRaycaster.Hit Raycast(
            Vector3 rayOrigin, Vector3 rayDir,
            Vector3 panelPos, Quaternion panelRot,
            float width, float height, float radius,
            int rtWidth, int rtHeight)
        {
            if (radius <= 0f || width <= 0f || height <= 0f) return default;

            // panel-local 化（回転は等長変換。width/height/radius は呼び出し側が lossyScale
            // 乗算済みの実ワールド寸法を渡す契約 → local で解いた t が world 距離とそのまま一致する）
            Quaternion inv = new Quaternion(-panelRot.x, -panelRot.y, -panelRot.z, panelRot.w);
            Vector3 o = inv * (rayOrigin - panelPos);
            Vector3 d = inv * rayDir;

            float alpha = (width * 0.5f) / radius;

            // 円筒軸 local (0,*,−R) を原点へ平行移動した xz 平面の二次方程式
            float cx = o.x;
            float cz = o.z + radius;
            float a = d.x * d.x + d.z * d.z;
            if (a < 1e-12f) return default; // 軸とほぼ平行（真上/真下向き）
            float b = 2f * (cx * d.x + cz * d.z);
            float c = cx * cx + cz * cz - radius * radius;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return default;

            float t = (-b + Mathf.Sqrt(disc)) / (2f * a); // 大きい根＝凹面側
            if (t < 0f) return default;

            Vector3 hit = o + d * t;
            float theta = Mathf.Atan2(hit.x, hit.z + radius);
            if (Mathf.Abs(theta) > alpha) return default;
            if (Mathf.Abs(hit.y) > height * 0.5f) return default;

            // 凹面（視点側）の法線 = 円筒軸→ヒット点の外向き radial を反転（local・|.|=1）→ world 化。
            Vector3 normal = panelRot * new Vector3(-hit.x / radius, 0f, -(hit.z + radius) / radius);
            if (Vector3.Dot(rayDir, normal) > 0f) normal = -normal; // 頑健化（常に ray 始点側）

            float u = (theta + alpha) / (2f * alpha);
            float v = hit.y / height + 0.5f;
            return new QuadRaycaster.Hit
            {
                Valid = true,
                Pixel = new Vector2(u * rtWidth, v * rtHeight),
                WorldPoint = rayOrigin + rayDir * t,
                Normal = normal,
            };
        }
    }
}
