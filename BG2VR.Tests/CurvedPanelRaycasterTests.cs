using UnityEngine;
using Xunit;
using BG2VR.VrInput;

namespace BG2VR.Tests
{
    public class CurvedPanelRaycasterTests
    {
        // 共通パネル: w=2, h=1, R=2 → α=0.5rad。RT=1920×1080。原点・無回転。
        private const float W = 2f, H = 1f, R = 2f;
        private static readonly Quaternion Id = new Quaternion(0f, 0f, 0f, 1f);

        private static QuadRaycaster.Hit Cast(Vector3 o, Vector3 d,
            Vector3 pos = default, Quaternion rot = default)
        {
            if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) rot = Id;
            return CurvedPanelRaycaster.Raycast(o, d, pos, rot, W, H, R, 1920, 1080);
        }

        [Fact]
        public void 正面中心命中はuv中央()
        {
            var hit = Cast(new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, 1f));
            Assert.True(hit.Valid);
            Assert.Equal(960f, hit.Pixel.x, 1);
            Assert.Equal(540f, hit.Pixel.y, 1);
            Assert.True((hit.WorldPoint - Vector3.zero).magnitude < 1e-4f);
        }

        [Fact]
        public void 既知角の命中はu期待値()
        {
            // θ=α/2=0.25rad の表面点: x=2sin(0.25)≈0.49481, z=2(cos(0.25)−1)≈−0.06224
            float x = 2f * Mathf.Sin(0.25f);
            float z = 2f * (Mathf.Cos(0.25f) - 1f);
            var hit = Cast(new Vector3(x, 0.2f, -5f), new Vector3(0f, 0f, 1f));
            Assert.True(hit.Valid);
            Assert.Equal(0.75f * 1920f, hit.Pixel.x, 0);          // u=(0.25+0.5)/1.0=0.75
            Assert.Equal((0.2f / H + 0.5f) * 1080f, hit.Pixel.y, 0); // v=0.7
            Assert.True(Mathf.Abs(hit.WorldPoint.z - z) < 1e-3f);
        }

        [Fact]
        public void 弧の外はmiss()
        {
            // θ=α=0.5 のさらに外側（x を端より大きく）
            var hit = Cast(new Vector3(1.2f, 0f, -5f), new Vector3(0f, 0f, 1f));
            Assert.False(hit.Valid);
        }

        [Fact]
        public void 高さの外はmiss()
        {
            var hit = Cast(new Vector3(0f, 0.6f, -2f), new Vector3(0f, 0f, 1f));
            Assert.False(hit.Valid);
        }

        [Fact]
        public void 後方rayはmiss()
        {
            var hit = Cast(new Vector3(0f, 0f, -2f), new Vector3(0f, 0f, -1f));
            Assert.False(hit.Valid);
        }

        [Fact]
        public void 視点が円筒軸より深い内側でも正面命中()
        {
            // 円筒軸は z=−R(−2)。それより遥かに手前（z=−10・円筒の外）からでも +z 側アークに正しく命中
            //（大きい根 t₂ が常にパネル側＝plan-review 🟡A の縮退確認）。
            var hit = Cast(new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, 1f));
            Assert.True(hit.Valid);
            Assert.Equal(960f, hit.Pixel.x, 1);
            Assert.Equal(540f, hit.Pixel.y, 1);
        }

        [Fact]
        public void 軸平行rayはmiss()
        {
            var hit = Cast(new Vector3(0f, -5f, 0f), new Vector3(0f, 1f, 0f));
            Assert.False(hit.Valid);
        }

        [Fact]
        public void 回転平行移動しても結果不変()
        {
            // 90° yaw + 平行移動でパネルとレイを丸ごと動かしても同じ u/v
            var rot = new Quaternion(0f, 0.70710678f, 0f, 0.70710678f);
            var pos = new Vector3(3f, 2f, 1f);
            Vector3 localO = new Vector3(2f * Mathf.Sin(0.25f), 0.2f, -5f);
            Vector3 localD = new Vector3(0f, 0f, 1f);
            var hit = Cast(pos + rot * localO, rot * localD, pos, rot);
            Assert.True(hit.Valid);
            Assert.Equal(0.75f * 1920f, hit.Pixel.x, 0);
            Assert.Equal((0.2f / H + 0.5f) * 1080f, hit.Pixel.y, 0);
        }

        // 実寸（lossyScale 乗算済み）width/height/radius を渡すと u 写像が正しくなる
        //（呼び出し側契約。α=(w/2)/R は s が分子分母で相殺され不変）。
        [Fact]
        public void Scaled_extents_map_u_by_actual_fraction()
        {
            // w=1, R=2 → α=0.25rad。θ=α/2=0.125 の位置 → u=0.75。scale s=0.855 を全寸法に乗算。
            float s = 0.855f, w = 1f, R = 2f, theta = 0.125f;
            float x = R * s * Mathf.Sin(theta);
            var h = CurvedPanelRaycaster.Raycast(
                new Vector3(x, 0, -1f), new Vector3(0, 0, 1),
                Vector3.zero, Quaternion.identity, w * s, 1f * s, R * s, 1920, 1080);
            Assert.True(h.Valid);
            Assert.Equal(1440f, h.Pixel.x, 1);
        }

        // 中央ヒットの法線は視点側（panel-local -Z = world -forward）。
        [Fact]
        public void Center_hit_normal_faces_viewer()
        {
            var h = CurvedPanelRaycaster.Raycast(
                new Vector3(0, 0, -1), new Vector3(0, 0, 1),
                Vector3.zero, Quaternion.identity, 1f, 1f, 2f, 1920, 1080);
            Assert.True(h.Valid);
            Assert.Equal(0f, h.Normal.x, 3);
            Assert.Equal(-1f, h.Normal.z, 3);
        }

        // 端寄りヒットの法線は弧に沿って傾く（θ の位置 → 法線 local = -(sinθ, 0, cosθ)）。
        [Fact]
        public void Edge_hit_normal_tilts_along_arc()
        {
            // w=1, R=2 → α=0.25rad。θ=α/2=0.125 の位置を +z レイで撃つ。
            float R = 2f, theta = 0.125f;
            float x = R * Mathf.Sin(theta);
            var h = CurvedPanelRaycaster.Raycast(
                new Vector3(x, 0, -1), new Vector3(0, 0, 1),
                Vector3.zero, Quaternion.identity, 1f, 1f, R, 1920, 1080);
            Assert.True(h.Valid);
            Assert.Equal(-Mathf.Sin(theta), h.Normal.x, 3);
            Assert.Equal(-Mathf.Cos(theta), h.Normal.z, 3);
            Assert.Equal(1f, h.Normal.magnitude, 3);
        }
    }
}
