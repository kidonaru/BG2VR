using System.Collections.Generic;
using UnityEngine;

namespace BG2VR.SpatialVoice
{
    /// <summary>
    /// 空間化ボイスの純関数群（UnityEngine + System のみ依存。BepInEx / native / ゲーム型に非依存）。
    /// テストは本クラスに対して行う（BG2VR.Tests は CoreModule のみ参照し Assembly-CSharp を引かないため、
    /// ゲームの GB.Game.CharID を直接は参照できない）。CharID 整数値はここに mirror し、ランナー側で
    /// 実 enum と一致を起動時検証する（<see cref="Speakers"/>）。
    /// </summary>
    internal static class SpatialVoiceLogic
    {
        /// <summary>
        /// 既知の話者キャラ名 → GB.Game.CharID 整数値の mirror（KANA=0..LUNA=5・NUM=6）。
        /// clip 名トークンの検証に使う。**GB.Game.CharID と一致を保つこと**（ランナーが起動時に実 enum と突き合わせる）。
        /// NUM はパース成功させるが、実シーンの m_characters に該当キャラが居らず head bone 解決で落ちる
        /// （= castResolved=false → 2D fallback）。これは仕様（システム voice の自然な除外）。
        /// </summary>
        internal static readonly IReadOnlyList<(string Name, int Id)> Speakers = new (string, int)[]
        {
            ("KANA", 0), ("RIN", 1), ("MIUKA", 2), ("ERISA", 3), ("KUON", 4), ("LUNA", 5), ("NUM", 6),
        };

        /// <summary>
        /// voice clip 名（"RIN_7290002_0_0_0_TEXT_ver2" 等）の先頭トークンから CharID 整数値を解決する。
        /// 先頭 '_' までを大文字小文字を区別して <see cref="Speakers"/> と照合する。
        /// 失敗（= 該当なし）は正常系: Bar イベントの数字 prefix（"810"/"710" 等）・空・null・小文字・未知名は false。
        /// 数字 prefix を弾くのは .NET の罠対策（Enum.TryParse は "810" を (CharID)810 として成功させてしまう）。
        /// </summary>
        public static bool TryCharIdFromClipName(string clipName, out int charId)
        {
            charId = -1;
            if (string.IsNullOrEmpty(clipName)) return false;
            int us = clipName.IndexOf('_');
            string token = us >= 0 ? clipName.Substring(0, us) : clipName;
            if (token.Length == 0) return false;
            for (int i = 0; i < Speakers.Count; i++)
            {
                if (Speakers[i].Name == token)
                {
                    charId = Speakers[i].Id;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// world 上の音源位置を HMD（リスナー）基準の Steam Audio 方向ベクトルへ変換する。
        /// listener space への変換は eye の world 軸（right/up/forward）への射影（Dot）で行う
        /// （Quaternion.Inverse は Unity の native ECall でテストホストから呼べないため・かつ Dot の方が安い）。
        /// Unity(左手・+Z 前) → Steam Audio(右手・−Z 前) の (x, y, −z) を適用し正規化。
        /// 返すのは「リスナー → 音源」への単位ベクトル（IPLBinauralEffectParams.direction の要件）。
        /// 退化（音源 = リスナー位置）時は正面 (0,0,−1) を返す。eye 軸は単位ベクトルであること。
        /// </summary>
        public static Vector3 WorldToSteamAudioDir(Vector3 castWorld, Vector3 eyePos,
            Vector3 eyeRight, Vector3 eyeUp, Vector3 eyeForward)
        {
            Vector3 world = castWorld - eyePos;
            // listener space（+X 右・+Y 上・+Z 前） = world を eye 軸へ射影
            float lx = Vector3.Dot(world, eyeRight);
            float ly = Vector3.Dot(world, eyeUp);
            float lz = Vector3.Dot(world, eyeForward);
            Vector3 sa = new Vector3(lx, ly, -lz); // Steam Audio は −Z 前
            return sa.sqrMagnitude > 1e-12f ? sa.normalized : new Vector3(0f, 0f, -1f);
        }

        /// <summary>
        /// 距離減衰ゲイン（0..1）。min 以内は 1（減衰なし）、max 以遠は 0（減衰しきる）、間は線形。
        /// binaural effect は距離減衰を持たないため、ミラー側で別途このゲインを掛ける。
        /// rig localScale で距離が world 単位スケールするため min/max は live config で吸収する。
        /// </summary>
        public static float DistanceGain(float dist, float min, float max)
        {
            if (dist <= min) return 1f;
            if (max <= min || dist >= max) return 0f;
            return 1f - (dist - min) / (max - min);
        }

        /// <summary>
        /// voice clip 名が ASMR ボイス（{CHAR}_5... 系）かのヒューリスティック判定。
        /// ASMR ミニゲーム・休日後の寝 ASMR とも MSGGROUP が "{id}_5{index}..." 系のため、2 番目トークンの
        /// 先頭が '5' なら ASMR とみなす（QueryConversationID.ASMR / HolidayAfterSleepASMR）。
        /// **clip 名規則依存＝ゲーム更新でズレうる**（ユーザー了承済み 2026-06-20）。ミニゲームの確実な検出は
        /// MiniGameProbe.IsAsmr（s_instance 型）で別途行い、本判定は会話経由 ASMR（寝 ASMR 等）を補う。
        /// </summary>
        public static bool IsAsmrVoiceClip(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            int us = clipName.IndexOf('_');
            if (us < 0 || us + 1 >= clipName.Length) return false; // '_' 無し / 2 番目トークン無し
            return clipName[us + 1] == '5'; // 2 番目トークンの先頭桁（カテゴリ）= 5 が ASMR 系
        }

        /// <summary>
        /// engage（本体ミュート＋ミラー HRTF）するか。全条件 AND の冪等判定。
        /// castResolved の権威は「head bone を解決できたか」（CharID パース成否ではない）。
        /// contextAllowed=false は抑制コンテキスト（ASMR / 目隠し鬼）＝本体素 2D に落とす。
        /// </summary>
        public static bool ShouldEngage(bool cfgOn, bool vrActive, bool eyeCamPresent,
            bool nativeLoaded, bool voicePlaying, bool castResolved, bool contextAllowed)
        {
            return cfgOn && vrActive && eyeCamPresent && nativeLoaded && voicePlaying && castResolved && contextAllowed;
        }
    }
}
