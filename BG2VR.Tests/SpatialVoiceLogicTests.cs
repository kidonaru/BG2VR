using BG2VR.SpatialVoice;
using UnityEngine;
using Xunit;

namespace BG2VR.Tests
{
    public class SpatialVoiceLogicTests
    {
        // ── TryCharIdFromClipName ───────────────────────────────
        [Theory]
        [InlineData("RIN_7290002_0_0_0_TEXT_ver2", 1)]
        [InlineData("KANA", 0)]              // '_' 無し = 文字列全体がトークン
        [InlineData("LUNA_4550810_0_0_0_TEXT", 5)]
        [InlineData("NUM_0001", 6)]          // パース成功（NUM=6）。head bone 未解決で 2D になるのはランナー側の別判定。
        public void TryCharIdFromClipName_Valid(string clip, int expected)
        {
            Assert.True(SpatialVoiceLogic.TryCharIdFromClipName(clip, out int id));
            Assert.Equal(expected, id);
        }

        [Theory]
        [InlineData("810")]                  // Bar イベントの数字 prefix（Enum.TryParse の罠を弾く）
        [InlineData("710_0_0")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("rin_x")]                // 小文字 = 大文字小文字を区別
        [InlineData("ZZZ_x")]                // 未知名
        [InlineData("_RIN")]                 // 先頭トークン空
        public void TryCharIdFromClipName_Invalid(string clip)
        {
            Assert.False(SpatialVoiceLogic.TryCharIdFromClipName(clip, out int id));
            Assert.Equal(-1, id);
        }

        // ── WorldToSteamAudioDir ────────────────────────────────
        // eye 軸は orthonormal basis を直接渡す（Quaternion.Inverse/Euler は Unity native ECall = テスト不可）。
        // identity basis: right=(1,0,0) up=(0,1,0) forward=(0,0,1)
        [Fact]
        public void WorldToSteamAudioDir_Front_IsNegativeZ()
        {
            // 真正面（Unity +Z）→ Steam Audio 前方 (0,0,-1)
            var d = SpatialVoiceLogic.WorldToSteamAudioDir(new Vector3(0, 0, 1), Vector3.zero,
                Vector3.right, Vector3.up, Vector3.forward);
            Assert.Equal(0f, d.x, 4);
            Assert.Equal(0f, d.y, 4);
            Assert.Equal(-1f, d.z, 4);
        }

        [Fact]
        public void WorldToSteamAudioDir_Right_IsPositiveX()
        {
            var d = SpatialVoiceLogic.WorldToSteamAudioDir(new Vector3(2, 0, 0), Vector3.zero,
                Vector3.right, Vector3.up, Vector3.forward);
            Assert.Equal(1f, d.x, 4);
            Assert.Equal(0f, d.y, 4);
            Assert.Equal(0f, d.z, 4);
        }

        [Fact]
        public void WorldToSteamAudioDir_Up_IsPositiveY()
        {
            var d = SpatialVoiceLogic.WorldToSteamAudioDir(new Vector3(0, 3, 0), Vector3.zero,
                Vector3.right, Vector3.up, Vector3.forward);
            Assert.Equal(0f, d.x, 4);
            Assert.Equal(1f, d.y, 4);
            Assert.Equal(0f, d.z, 4);
        }

        [Fact]
        public void WorldToSteamAudioDir_RotatedListener_AccountsForHeadYaw()
        {
            // リスナーが +X を向く（yaw 90°）: forward=(1,0,0) right=(0,0,-1) up=(0,1,0)。
            // world +X の音源は正面 → Steam Audio (0,0,-1)
            var d = SpatialVoiceLogic.WorldToSteamAudioDir(new Vector3(5, 0, 0), Vector3.zero,
                new Vector3(0, 0, -1), Vector3.up, new Vector3(1, 0, 0));
            Assert.Equal(0f, d.x, 4);
            Assert.Equal(0f, d.y, 4);
            Assert.Equal(-1f, d.z, 4);
        }

        [Fact]
        public void WorldToSteamAudioDir_Degenerate_ReturnsFront()
        {
            var d = SpatialVoiceLogic.WorldToSteamAudioDir(Vector3.one, Vector3.one,
                Vector3.right, Vector3.up, Vector3.forward);
            Assert.Equal(0f, d.x, 4);
            Assert.Equal(0f, d.y, 4);
            Assert.Equal(-1f, d.z, 4);
        }

        [Fact]
        public void WorldToSteamAudioDir_IsUnitLength()
        {
            var d = SpatialVoiceLogic.WorldToSteamAudioDir(new Vector3(3, -4, 12), new Vector3(1, 1, 1),
                Vector3.right, Vector3.up, Vector3.forward);
            Assert.Equal(1f, d.magnitude, 4);
        }

        // ── DistanceGain ────────────────────────────────────────
        [Theory]
        [InlineData(0f, 1f, 5f, 1f)]
        [InlineData(1f, 1f, 5f, 1f)]
        [InlineData(3f, 1f, 5f, 0.5f)]
        [InlineData(5f, 1f, 5f, 0f)]
        [InlineData(6f, 1f, 5f, 0f)]
        [InlineData(3f, 2f, 2f, 0f)]   // max<=min（誤設定）かつ dist>min → 0
        [InlineData(1f, 2f, 2f, 1f)]   // dist<=min は範囲に依らず 1
        public void DistanceGain_Boundaries(float dist, float min, float max, float expected)
        {
            Assert.Equal(expected, SpatialVoiceLogic.DistanceGain(dist, min, max), 4);
        }

        // ── IsAsmrVoiceClip（ヒューリスティック: 2 番目トークン先頭 = '5'） ──
        [Theory]
        [InlineData("RIN_5200001_0_0_0_TEXT", true)]        // ASMR ミニゲームボイス（_5 系）
        [InlineData("KANA_5150001_0_0", true)]              // 休日後の寝 ASMR（_5 系）
        [InlineData("RIN_7290002_0_0_0_TEXT_ver2", false)]  // 通常会話（_7）
        [InlineData("RIN_4250810_0_0_0_TEXT", false)]       // Bar イベント（_4）
        [InlineData("810", false)]                          // 数字 prefix（'_' 無し）
        [InlineData("RIN", false)]                          // 2 番目トークン無し
        [InlineData("RIN_", false)]                         // 区切りのみ
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsAsmrVoiceClip_Heuristic(string clip, bool expected)
        {
            Assert.Equal(expected, SpatialVoiceLogic.IsAsmrVoiceClip(clip));
        }

        // ── ShouldEngage ────────────────────────────────────────
        [Fact]
        public void ShouldEngage_AllTrue_Engages()
        {
            Assert.True(SpatialVoiceLogic.ShouldEngage(true, true, true, true, true, true, true));
        }

        [Theory]
        [InlineData(false, true, true, true, true, true, true)]  // cfg OFF
        [InlineData(true, false, true, true, true, true, true)]  // 非 VR
        [InlineData(true, true, false, true, true, true, true)]  // eyeCam 不在（teardown 中）
        [InlineData(true, true, true, false, true, true, true)]  // native 未ロード
        [InlineData(true, true, true, true, false, true, true)]  // voice 非再生
        [InlineData(true, true, true, true, true, false, true)]  // cast 未解決（head bone 不在）
        [InlineData(true, true, true, true, true, true, false)]  // 抑制コンテキスト（ASMR / 目隠し鬼）
        public void ShouldEngage_AnyFalse_Disengages(bool a, bool b, bool c, bool d, bool e, bool f, bool g)
        {
            Assert.False(SpatialVoiceLogic.ShouldEngage(a, b, c, d, e, f, g));
        }
    }
}
