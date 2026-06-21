using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

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

        // 配布/新規環境向けの初期固定位置は埋め込みリソース Resources/default_pinned_poses.json から読む
        // （ファイル未作成時にシード→materialize）。ユーザーが一度でも保存/消去すればファイルが存在し
        // 以後はそれが唯一の真＝この既定は上書きしない。同梱 shader/model と同じ manifest resource 方式。
        private const string DefaultPosesResourceSuffix = "default_pinned_poses.json";

        // 埋め込みリソースから既定固定位置 JSON を取り出す。取得できなければ null（呼び出し側で空シード扱い）。
        private static string TryLoadDefaultPosesJson()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string resName = null;
                foreach (var n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith(DefaultPosesResourceSuffix)) { resName = n; break; }
                }
                if (resName == null) return null;
                using (var s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null) return null;
                    using (var reader = new StreamReader(s))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[ScenePinnedPose] 既定リソース読込で例外（空で継続）: {ex.Message}");
                return null;
            }
        }

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
                    string defaultJson = TryLoadDefaultPosesJson();
                    if (defaultJson == null)
                    {
                        // リソース欠落＝ビルド構成（EmbeddedResource 漏れ）を疑う。空のまま継続し Save しない
                        // （空ファイルで将来の修正を shadow しない＝次回起動で再シードを試みる）。
                        Plugin.Log.LogWarning("[ScenePinnedPose] 既定固定位置リソースが見つからない（ビルド構成＝EmbeddedResource 漏れを疑え）。空で継続。");
                        m_poses = new Dictionary<string, PinnedPose>();
                        return;
                    }
                    m_poses = PinnedPoseCodec.Parse(defaultJson);
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
