using UnityEngine;
using Xunit;
using BG2VR.WorldUi;

namespace BG2VR.Tests
{
    public class ScaleDragSolverTests
    {
        [Fact]
        public void ピッチ不変なら恒等()
        {
            Assert.Equal(1.2f, ScaleDragSolver.Solve(1.2f, 0.3f, 0.3f, 0.4f, 3f), 4);
        }

        [Fact]
        public void 上で拡大_下で縮小_指数対称()
        {
            float up = ScaleDragSolver.Solve(1f, 0f, 0.2f, 0.01f, 100f);
            float down = ScaleDragSolver.Solve(1f, 0f, -0.2f, 0.01f, 100f);
            Assert.True(up > 1f);
            Assert.True(down < 1f);
            Assert.Equal(1f, up * down, 3); // e^x · e^−x = 1
        }

        [Fact]
        public void ピッチ差に対し単調増加()
        {
            float prev = 0f;
            for (int i = -3; i <= 3; i++)
            {
                float s = ScaleDragSolver.Solve(1f, 0f, i * 0.1f, 0.01f, 100f);
                Assert.True(s > prev);
                prev = s;
            }
        }

        [Fact]
        public void 範囲でclampされる()
        {
            Assert.Equal(3f, ScaleDragSolver.Solve(2.5f, 0f, 1.5f, 0.4f, 3f), 4);
            Assert.Equal(0.4f, ScaleDragSolver.Solve(0.5f, 0f, -1.5f, 0.4f, 3f), 4);
        }

        [Fact]
        public void Pitchはdiryのasin()
        {
            Assert.Equal(Mathf.Asin(0.5f), ScaleDragSolver.Pitch(new Vector3(0f, 0.5f, 0.8660254f)), 4);
            Assert.Equal(0f, ScaleDragSolver.Pitch(Vector3.zero), 4); // 退化はゼロ
        }
    }
}
