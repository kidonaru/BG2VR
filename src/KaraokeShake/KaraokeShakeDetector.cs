using UnityEngine;

namespace BG2VR.KaraokeShake
{
    /// <summary>
    /// カラオケ振り入力の検知（純関数・per-hand・BepInEx 非依存でテスト可能）。
    /// 下/前方向の線速度 + 手首スナップ（角速度 magnitude）を重み付き合算したスコアが
    /// High しきい値を超えた瞬間に 1 発発火する。上向き（持ち上げ）は下/前項が 0 になり
    /// 構造的に不発（要件の核）。1スイング1発は「発火→不応期→低スコアで再武装」の状態機械で担保。
    /// </summary>
    public static class KaraokeShakeDetector
    {
        public struct Params
        {
            public float High;          // 発火しきい値（混合スコア・実機チューニング）
            public float ReleaseRatio;  // 再武装しきい値 = High * ReleaseRatio
            public float RefractorySec; // 不応期（秒）
            public float DownWeight;    // 下向き線速度の重み
            public float ForwardWeight; // 前向き線速度の重み
            public float AngularWeight; // 角速度(rad/s) magnitude の重み
            public float LiftVetoSpeed; // 上向き線速度がこの値(m/s)を超えたら角速度項を無効化（持ち上げ誤発火防止）
            public float Smoothing;     // EMA alpha 0..1（1=平滑なし）
        }

        public struct State
        {
            public Vector3 SmoothedLinVel;
            public float SmoothedAngSpeed;
            public bool Armed;
            public float RefractoryTimer;
            public bool Initialized;
        }

        /// <summary>初期 State（カラオケ離脱時の再生成にも使う）。</summary>
        public static State NewState() => new State { Armed = true, Initialized = false };

        /// <param name="linVel">rig-local 線速度 m/s（y=rig 上方向, z=rig 前方向）</param>
        /// <param name="angSpeed">角速度 magnitude rad/s（座標系不変なスカラ）</param>
        /// <param name="dt">フレーム時間（秒）</param>
        /// <returns>このフレームで音符入力を発火するか</returns>
        public static bool Step(ref State s, Vector3 linVel, float angSpeed, float dt, in Params p)
        {
            // 初回は平滑のシードのみ（速度の初期過渡で誤発火しない）。
            if (!s.Initialized)
            {
                s.SmoothedLinVel = linVel;
                s.SmoothedAngSpeed = angSpeed;
                s.Armed = true;
                s.RefractoryTimer = 0f;
                s.Initialized = true;
                return false;
            }

            float a = Mathf.Clamp01(p.Smoothing);
            s.SmoothedLinVel = Vector3.Lerp(s.SmoothedLinVel, linVel, a);
            s.SmoothedAngSpeed = Mathf.Lerp(s.SmoothedAngSpeed, angSpeed, a);

            // 角速度(手首スナップ)項は方向を持たないため、上向き(持ち上げ)中は無効化する。
            // これが無いと持ち上げ中の手首回転で誤発火し「持ち上げで反応しない」要件が崩れる。
            // 線速度の下/前項は max(0,…) で元から上向きに不感（構造的保証）。
            float upSpeed = Mathf.Max(0f, s.SmoothedLinVel.y);
            float angContribution = (upSpeed <= p.LiftVetoSpeed) ? s.SmoothedAngSpeed * p.AngularWeight : 0f;

            float score = Mathf.Max(0f, -s.SmoothedLinVel.y) * p.DownWeight
                        + Mathf.Max(0f,  s.SmoothedLinVel.z) * p.ForwardWeight
                        + angContribution;

            if (s.RefractoryTimer > 0f)
                s.RefractoryTimer = Mathf.Max(0f, s.RefractoryTimer - dt);

            if (s.Armed)
            {
                if (score >= p.High)
                {
                    s.Armed = false;
                    s.RefractoryTimer = p.RefractorySec;
                    return true;
                }
            }
            else if (s.RefractoryTimer <= 0f && score <= p.High * p.ReleaseRatio)
            {
                s.Armed = true;
            }
            return false;
        }
    }
}
