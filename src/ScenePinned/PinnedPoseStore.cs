using System;
using System.Collections.Generic;
using System.IO;

namespace BG2VR.ScenePinned
{
    /// <summary>
    /// 固定 pose の in-memory store ＋ JSON サイドカー永続化（impure shell・spec §4.4）。
    /// パス: &lt;BepInEx config&gt;/BG2VR.ScenePinnedPoses.json。
    /// serialize/parse は純関数 PinnedPoseCodec に委譲。I/O 失敗はログのみ（abort しないベストエフォート）。
    /// </summary>
    public sealed class PinnedPoseStore
    {
        private readonly string m_path;
        private Dictionary<string, PinnedPose> m_poses = new Dictionary<string, PinnedPose>();

        // 配布/新規環境向けの初期固定位置（ファイル未作成時にシード→materialize する）。
        // 実機チューニング済みの基準値（採取 2026-06-19）。GameRoomScene の通常入室は未調整のため含めない
        // （各ミニゲーム単位＝.HAND_SUMO/.CHEKI/.TWISTER/.KARUTA は調整済みで含める）＝通常入室は入場時カメラ追従。
        // ユーザーが一度でも保存/消去すればファイルが存在し以後はそれが唯一の真＝この既定は上書きしない。
        // Parse でそのまま復元するため整形 JSON で可読に保持する。
        private const string DefaultPosesJson = @"{
  ""version"": 1,
  ""poses"": {
    ""Talk2DScene"": { ""x"": -0.04806567, ""y"": -0.753840864, ""z"": 0.943841338, ""yaw"": 179.604874 },
    ""HoleScene"": { ""x"": 0.111833528, ""y"": 0.6694112, ""z"": 2.29016614, ""yaw"": 214.4362 },
    ""VipRoomScene"": { ""x"": -2.43037176, ""y"": 0.06902969, ""z"": -3.56335068, ""yaw"": 241.074036 },
    ""VipRoomScene.KARAOKE"": { ""x"": -3.73395729, ""y"": 0.576210439, ""z"": -3.20451379, ""yaw"": 317.53772 },
    ""GameRoomScene.HAND_SUMO"": { ""x"": -3.1706717, ""y"": 0.499099851, ""z"": -3.50250816, ""yaw"": 267.694122 },
    ""VipRoomScene.AHHN_GAME"": { ""x"": -4.93070745, ""y"": 0.558600962, ""z"": -4.843435, ""yaw"": 225.558548 },
    ""HoleScene.AHHN_GAME"": { ""x"": -0.01632461, ""y"": 0.712179661, ""z"": 2.1764853, ""yaw"": 217.155426 },
    ""GameRoomScene.CHEKI"": { ""x"": -2.76772833, ""y"": 0.358624339, ""z"": -3.537246, ""yaw"": 273.2741 },
    ""GameRoomScene.TWISTER"": { ""x"": -2.72274756, ""y"": 0.06297487, ""z"": -3.4721818, ""yaw"": 265.827026 },
    ""GameRoomScene.KARUTA"": { ""x"": -2.934013, ""y"": -0.451829135, ""z"": -3.47705579, ""yaw"": 271.2976 }
  }
}";

        public PinnedPoseStore(string path) { m_path = path; }

        public static string DefaultPath() =>
            Path.Combine(BepInEx.Paths.ConfigPath, "BG2VR.ScenePinnedPoses.json");

        public bool TryGet(string key, out PinnedPose pose) => m_poses.TryGetValue(key, out pose);

        public void Set(string key, PinnedPose pose) { m_poses[key] = pose; Save(); }

        public void Remove(string key) { if (m_poses.Remove(key)) Save(); }

        public void Load()
        {
            try
            {
                if (!File.Exists(m_path))
                {
                    // ファイル未作成（初回/配布直後）: 既定の固定位置をシードして materialize する。
                    m_poses = PinnedPoseCodec.Parse(DefaultPosesJson);
                    Plugin.Log.LogInfo($"[ScenePinnedPose] 固定位置ファイル未作成→既定 {m_poses.Count} 件をシードして作成: {m_path}");
                    Save();
                    return;
                }
                m_poses = PinnedPoseCodec.Parse(File.ReadAllText(m_path));
                Plugin.Log.LogInfo($"[ScenePinnedPose] 固定位置を読込: {m_poses.Count} 件 ({m_path})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ScenePinnedPose] 読込失敗（空で継続）: {ex.Message}");
                m_poses = new Dictionary<string, PinnedPose>();
            }
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(m_path, PinnedPoseCodec.Serialize(m_poses));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ScenePinnedPose] 保存失敗: {ex.Message}");
            }
        }
    }
}
