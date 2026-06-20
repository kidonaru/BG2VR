using UnityEngine;
using Xunit;
using BG2VR.WorldUi;

namespace BG2VR.Tests
{
    public class PanelMeshBuilderTests
    {
        [Fact]
        public void Flat_は4頂点2トライアングルで四隅一致_z0()
        {
            var d = PanelMeshBuilder.BuildFlat(2f, 1f);
            Assert.Equal(4, d.Vertices.Length);
            Assert.Equal(6, d.Triangles.Length);
            foreach (var v in d.Vertices) Assert.Equal(0f, v.z, 3);
            // 四隅（実寸・中心原点）
            Assert.Contains(d.Vertices, v => (v - new Vector3(-1f, -0.5f, 0f)).magnitude < 1e-4f);
            Assert.Contains(d.Vertices, v => (v - new Vector3(1f, 0.5f, 0f)).magnitude < 1e-4f);
        }

        [Fact]
        public void Flat_のUVは左下00右上11()
        {
            var d = PanelMeshBuilder.BuildFlat(2f, 1f);
            for (int i = 0; i < d.Vertices.Length; i++)
            {
                float expU = d.Vertices[i].x / 2f + 0.5f;
                float expV = d.Vertices[i].y / 1f + 0.5f;
                Assert.Equal(expU, d.Uvs[i].x, 4);
                Assert.Equal(expV, d.Uvs[i].y, 4);
            }
        }

        [Fact]
        public void Curved_は弧長保存で凹面が視点側()
        {
            float w = 2f, h = 1f, R = 2f;
            var d = PanelMeshBuilder.BuildCurved(w, h, R);
            Assert.Equal((PanelMeshBuilder.CurvedSegments + 1) * 2, d.Vertices.Length);
            Assert.Equal(PanelMeshBuilder.CurvedSegments * 6, d.Triangles.Length);

            // 下行（先頭 cols 個）の隣接頂点間距離の和 ≈ 弧長 = w（弦近似誤差 << 0.002）
            int cols = PanelMeshBuilder.CurvedSegments + 1;
            float sum = 0f;
            for (int i = 0; i < cols - 1; i++)
                sum += (d.Vertices[i + 1] - d.Vertices[i]).magnitude;
            Assert.True(Mathf.Abs(sum - w) < 0.002f, $"弧長近似={sum}");

            // z ≤ 0（凹面が -Z=視点側）・中央列 z=0
            foreach (var v in d.Vertices) Assert.True(v.z <= 1e-6f, $"z={v.z}");
            Assert.Equal(0f, d.Vertices[cols / 2].z, 4); // 中央列（cols=33 → index16 = θ=0）
        }

        [Fact]
        public void Curved_のUVは単調で0と1に到達()
        {
            var d = PanelMeshBuilder.BuildCurved(2f, 1f, 2f);
            int cols = PanelMeshBuilder.CurvedSegments + 1;
            Assert.Equal(0f, d.Uvs[0].x, 4);
            Assert.Equal(1f, d.Uvs[cols - 1].x, 4);
            for (int i = 0; i < cols - 1; i++)
                Assert.True(d.Uvs[i + 1].x > d.Uvs[i].x, "u が単調増加でない");
            Assert.Equal(0f, d.Uvs[0].y, 4);          // 下行 v=0
            Assert.Equal(1f, d.Uvs[cols].y, 4);        // 上行 v=1
        }

        [Fact]
        public void Curved_は大半径で平面に漸近()
        {
            var flat = PanelMeshBuilder.BuildFlat(2f, 1f);
            var curved = PanelMeshBuilder.BuildCurved(2f, 1f, 1000f);
            int cols = PanelMeshBuilder.CurvedSegments + 1;
            // 端点同士を比較（flat は 4 頂点なので対応点で）
            Assert.True((curved.Vertices[0] - flat.Vertices[0]).magnitude < 1e-3f);
            Assert.True((curved.Vertices[cols - 1] - flat.Vertices[1]).magnitude < 1e-3f);
        }
    }
}
