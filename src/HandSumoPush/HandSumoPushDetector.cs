using UnityEngine;

namespace BG2VR.HandSumoPush
{
    /// <summary>
    /// 手押し相撲「両手を前に出す」入力の検知（純関数・両手 AND・BepInEx 非依存でテスト可能）。
    /// 各手の前方向(rig +z)線速度を EMA 平滑し、しきい値超えで per-hand の「押し出し timer」を立てる。
    /// 両手の timer が同時に生存（＝同時性窓 CoincidenceSec 以内に両手とも前進）し armed のとき 1 発発火する。
    /// 後方/上方の運動は max(0, z) で構造的に不感。1 押し 1 発は「発火→不応期→両手低速で再武装」で担保。
    /// 注: 片手を release 比以下に戻さず保持したまま（push timer 生存中に）もう片手を一瞬前進させても、窓内なら
    /// AND が成立して発火しうる。「両手同時」を厳密に保ちたい場合は CoincidenceSec ≤ RefractorySec に保つ
    /// （既定 0.25 ≤ 0.3＝発火後の不応期が窓より長く、片手保持での連発を防ぐ）。
    /// </summary>
    public static class HandSumoPushDetector
    {
        public struct Params
        {
            public float High;           // 前進速度の発火しきい値（m/s）
            public float ReleaseRatio;   // 再武装しきい値 = High * ReleaseRatio
            public float RefractorySec;  // 不応期（秒）
            public float CoincidenceSec; // 両手同時性窓（秒）
            public float Smoothing;      // EMA alpha 0..1（1=平滑なし）
        }

        public struct State
        {
            public float LeftFwd;        // 平滑前進速度（左・m/s）
            public float RightFwd;       // 平滑前進速度（右）
            public float LeftPushTimer;  // 左の押し出し窓タイマ（>0 で押し出し有効中）
            public float RightPushTimer; // 右の押し出し窓タイマ
            public bool Armed;
            public float RefractoryTimer;
            public bool Initialized;
        }

        /// <summary>初期 State（手押し相撲離脱時の再生成にも使う）。</summary>
        public static State NewState() => new State { Armed = true, Initialized = false };

        /// <param name="leftLinVel">左手 rig-local 線速度 m/s（z=rig 前方向）</param>
        /// <param name="rightLinVel">右手 rig-local 線速度 m/s</param>
        /// <param name="dt">フレーム時間（秒）</param>
        /// <returns>このフレームでクリック（ATriggered）を発火するか</returns>
        public static bool Step(ref State s, Vector3 leftLinVel, Vector3 rightLinVel, float dt, in Params p)
        {
            // 前進成分のみ（後方/上方は構造的に 0＝不感）。
            float leftFwd = Mathf.Max(0f, leftLinVel.z);
            float rightFwd = Mathf.Max(0f, rightLinVel.z);

            // 初回は平滑のシードのみ（速度の初期過渡で誤発火しない）。
            if (!s.Initialized)
            {
                s.LeftFwd = leftFwd;
                s.RightFwd = rightFwd;
                s.LeftPushTimer = 0f;
                s.RightPushTimer = 0f;
                s.Armed = true;
                s.RefractoryTimer = 0f;
                s.Initialized = true;
                return false;
            }

            float a = Mathf.Clamp01(p.Smoothing);
            s.LeftFwd = Mathf.Lerp(s.LeftFwd, leftFwd, a);
            s.RightFwd = Mathf.Lerp(s.RightFwd, rightFwd, a);

            // 押し出し窓タイマ: しきい値超えで窓を張り直し、それ以外は減衰（窓内なら前進が止んでも生存）。
            if (s.LeftFwd >= p.High) s.LeftPushTimer = p.CoincidenceSec;
            else s.LeftPushTimer = Mathf.Max(0f, s.LeftPushTimer - dt);

            if (s.RightFwd >= p.High) s.RightPushTimer = p.CoincidenceSec;
            else s.RightPushTimer = Mathf.Max(0f, s.RightPushTimer - dt);

            if (s.RefractoryTimer > 0f)
                s.RefractoryTimer = Mathf.Max(0f, s.RefractoryTimer - dt);

            float release = p.High * p.ReleaseRatio;

            if (s.Armed)
            {
                // 両手とも押し出し窓が生存＝同時性窓内に両手が前進した → 1 発。
                if (s.RefractoryTimer <= 0f && s.LeftPushTimer > 0f && s.RightPushTimer > 0f)
                {
                    s.Armed = false;
                    s.RefractoryTimer = p.RefractorySec;
                    return true;
                }
            }
            else if (s.RefractoryTimer <= 0f && s.LeftFwd <= release && s.RightFwd <= release)
            {
                // 両手とも十分減速したら再武装（1 押し 1 発）。
                s.Armed = true;
                // 前回押しの残存窓を破棄。これが無いと、長押しの解放 tail で stale な push timer が
                // 不応期明けまで生存し、再武装直後のフレームで AND が成立して 2 発目が誤発火する。
                s.LeftPushTimer = 0f;
                s.RightPushTimer = 0f;
            }
            return false;
        }
    }
}
