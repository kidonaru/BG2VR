using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BG2VR.ScenePinned
{
    /// <summary>env キーごとの固定 pose（ワールド絶対 position + yaw）。pitch/roll は持たない（rig は直立）。</summary>
    public readonly struct PinnedPose
    {
        public readonly Vector3 Position;
        public readonly float Yaw;
        public PinnedPose(Vector3 position, float yaw) { Position = position; Yaw = yaw; }
    }

    /// <summary>
    /// 固定 pose ストアの JSON serialize/parse（純関数・xUnit 対象。spec §4.3）。
    /// スキーマ: {"version":1,"poses":{"&lt;envKey&gt;":{"x":..,"y":..,"z":..,"yaw":..}, ...}}
    /// Parse は不正/欠損にベストエフォート（throw せず空 or 当該エントリ skip）。
    /// 数値は invariant culture（小数点コンマ環境でも壊れない）。
    /// キーは env scene 型名（[A-Za-z0-9_]）、またはミニゲーム時は "型名.MINIGAME" の複合（"." 区切り）。
    /// いずれも JSON 特殊文字を含まず＝エスケープ不要（parse の regex は [^"]+ で対応済）。
    /// </summary>
    public static class PinnedPoseCodec
    {
        // 2スペースインデントで整形出力（人が cfg ディレクトリで開いて読める形。Parse は \s* 許容で往復可）。
        // 1 env=1 行のインライン object（座標 4 つは横並びの方が読みやすいため leaf は展開しない）。改行は \n 固定
        // （Environment.NewLine 依存にすると出力がプラットフォーム差で非決定になりテストが不安定化するため）。
        public static string Serialize(IReadOnlyDictionary<string, PinnedPose> poses)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": 1,\n");
            sb.Append("  \"poses\": {");
            bool first = true;
            foreach (var kv in poses)
            {
                sb.Append(first ? "\n" : ",\n");
                first = false;
                sb.Append("    \"").Append(kv.Key).Append("\": { ");
                sb.Append("\"x\": ").Append(F(kv.Value.Position.x)).Append(", ");
                sb.Append("\"y\": ").Append(F(kv.Value.Position.y)).Append(", ");
                sb.Append("\"z\": ").Append(F(kv.Value.Position.z)).Append(", ");
                sb.Append("\"yaw\": ").Append(F(kv.Value.Yaw)).Append(" }");
            }
            if (!first) sb.Append("\n  "); // 空でなければ poses 閉じ括弧を改行+インデント
            sb.Append("}\n}");
            return sb.ToString();
        }

        // .NET の float.ToString("R") は near-zero 値を負の指数表記（例 -3.2E-07）で書くため、
        // 指数部の符号 [+-] まで取り込む数値パターンにする。旧 [0-9.eE+] は E 直後の '-' で停止し
        // 当該エントリが脱落＝保存 pose の静かな消失バグだった。captured 文字列は最終的に
        // float.TryParse で再検証するため、緩めの形でも不正値は安全に skip される。
        // 注: NaN/Infinity は英字トークンで本パターンに match しないため regex 段で skip される
        //（pose 座標に非有限値は保存しない設計＝破損データを取り込まない方が正しい）。
        private const string Num = "-?(?:[0-9]+\\.?[0-9]*|\\.[0-9]+)(?:[eE][+-]?[0-9]+)?";

        // 各 pose エントリ（leaf object）を抽出する。version 数値や "poses" wrapper は形が違うため誤 match しない。
        private static readonly Regex EntryRegex = new Regex(
            "\"(?<key>[^\"]+)\"\\s*:\\s*\\{\\s*" +
            "\"x\"\\s*:\\s*(?<x>" + Num + ")\\s*,\\s*" +
            "\"y\"\\s*:\\s*(?<y>" + Num + ")\\s*,\\s*" +
            "\"z\"\\s*:\\s*(?<z>" + Num + ")\\s*,\\s*" +
            "\"yaw\"\\s*:\\s*(?<yaw>" + Num + ")\\s*\\}",
            RegexOptions.Compiled);

        public static Dictionary<string, PinnedPose> Parse(string json)
        {
            var result = new Dictionary<string, PinnedPose>();
            if (string.IsNullOrEmpty(json)) return result;
            foreach (Match m in EntryRegex.Matches(json))
            {
                if (!TryF(m.Groups["x"].Value, out float x)) continue;
                if (!TryF(m.Groups["y"].Value, out float y)) continue;
                if (!TryF(m.Groups["z"].Value, out float z)) continue;
                if (!TryF(m.Groups["yaw"].Value, out float yaw)) continue;
                result[m.Groups["key"].Value] = new PinnedPose(new Vector3(x, y, z), yaw);
            }
            return result;
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static bool TryF(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
