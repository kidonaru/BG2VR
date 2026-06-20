using UnityEngine;

namespace BG2VR.WorldUi
{
    /// <summary>
    /// パネルメッシュ（平面 / 縦軸円筒弧）の頂点・UV・index を panel-local 実寸(m)で生成する純関数。
    /// 読み面は −Z（Unity Quad と同じ向き＝既存の配置回転ロジックを変えない）。
    /// 曲面は弧長＝幅を保存（半角 α = (w/2)/R）・x=R·sinθ, z=R·(cosθ−1) ≤ 0 ＝凹面が視点側。
    /// UV は u=(θ+α)/(2α)（弧長均等＝横歪みなし・平面の u=x/w+0.5 と端点一致）。
    /// spec: docs/superpowers/specs/2026-06-06-bg2-vr-ui-adjust-buttons-design.md §4
    /// </summary>
    public static class PanelMeshBuilder
    {
        public const int CurvedSegments = 32;

        public struct MeshData
        {
            public Vector3[] Vertices;
            public Vector2[] Uvs;
            public int[] Triangles;
        }

        /// <summary>平面（2 トライアングル）。</summary>
        public static MeshData BuildFlat(float width, float height) => Build(width, height, 0f, 1);

        /// <summary>円筒弧（弧長=width 保存・半径 radius）。</summary>
        public static MeshData BuildCurved(float width, float height, float radius)
            => Build(width, height, radius, CurvedSegments);

        private static MeshData Build(float width, float height, float radius, int segments)
        {
            int cols = segments + 1;
            var verts = new Vector3[cols * 2];
            var uvs = new Vector2[cols * 2];
            float hh = height * 0.5f;
            bool curved = radius > 0f;
            float alpha = curved ? (width * 0.5f) / radius : 0f;

            for (int i = 0; i < cols; i++)
            {
                float u = (float)i / segments;
                float x, z;
                if (curved)
                {
                    float theta = -alpha + 2f * alpha * u;
                    x = radius * Mathf.Sin(theta);
                    z = radius * (Mathf.Cos(theta) - 1f);
                }
                else
                {
                    x = -width * 0.5f + width * u;
                    z = 0f;
                }
                verts[i] = new Vector3(x, -hh, z);        // 下行
                verts[cols + i] = new Vector3(x, hh, z);  // 上行
                uvs[i] = new Vector2(u, 0f);
                uvs[cols + i] = new Vector2(u, 1f);
            }

            // 読み面 −Z: −Z 側から見て時計回り＝(bl, tl, br), (br, tl, tr)
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int bl = i, br = i + 1, tl = cols + i, tr = cols + i + 1;
                int o = i * 6;
                tris[o + 0] = bl; tris[o + 1] = tl; tris[o + 2] = br;
                tris[o + 3] = br; tris[o + 4] = tl; tris[o + 5] = tr;
            }

            return new MeshData { Vertices = verts, Uvs = uvs, Triangles = tris };
        }
    }
}
