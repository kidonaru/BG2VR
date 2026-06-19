using UnityEngine;

namespace BG2VR.HeadLook
{
    /// <summary>
    /// 顔/視線 HMD 追従の純関数 solver（spec §5.2）。
    /// native ECall 禁止（テストホスト制約）: Vector3 演算 + Mathf のみで書く。
    /// 回転の合成（AngleAxis 等）・UV 変換は runner 側の責務。
    /// </summary>
    public static class HeadLookSolver
    {
        // チューニング値はすべて Tuning struct 経由（既定値は Configs.yaml が単一の定義元）

        public struct Angles
        {
            public float Yaw;   // 度。右が正
            public float Pitch; // 度。上が正
        }

        /// <summary>
        /// Config 化されたチューニング値の束（既定値は Configs.yaml が単一の定義元。
        /// runner が解決済み値で埋めて渡し、テストはテスト側 fixture を使う）。
        /// release 境界は engage + 固定マージンで導出し、release ≤ engage の誤設定によるパタつき破綻を
        /// 構造的に防止する。
        /// </summary>
        public struct Tuning
        {
            public float EngageYawDeg;         // 追従開始: |yaw| ≤ これ（度）
            public float EngagePitchDeg;       // 追従開始: |pitch| ≤ これ（度）
            public float ReleaseYawMarginDeg;  // 解除境界 = engage + これ（パタつき防止ヒステリシス）
            public float ReleasePitchMarginDeg;// 同上（pitch 側）
            public float DeadZoneStartDeg;     // 首: ズレがこれを超えたら動き始める（度）
            public float DeadZoneStopDeg;      // 首: ズレがこれ以下で停止（度・Start 超は Start に丸め）
            public float HeadTau;              // 首の指数平滑 時定数（秒）
            public float EyeTau;               // 目の吸着 時定数（秒）
            public float HeadRatio;            // 首の適用率
            public float EyeYawRatio;          // 目の適用率（左右）
            public float EyePitchRatio;        // 目の適用率（上下）

            public float ReleaseYawDeg => EngageYawDeg + ReleaseYawMarginDeg;
            public float ReleasePitchDeg => EngagePitchDeg + ReleasePitchMarginDeg;
        }

        public struct StepResult
        {
            public float HeadYawApplied;  // 度。bone へ合成する最終角（適用率込み）
            public float HeadPitchApplied;
            public float EyeYawApplied;   // 度。眼球角（適用率込み）。runner が UV に変換
            public float EyePitchApplied;
        }

        /// <summary>
        /// head の素ポーズ軸（world）基準で、target への yaw/pitch オフセット角（度）を返す。
        /// fwd/up/right は正規直交前提（runner が bone 回転から作る）。
        /// </summary>
        public static Angles ComputeOffsetAngles(
            Vector3 headPos, Vector3 fwd, Vector3 up, Vector3 right, Vector3 targetPos)
        {
            Vector3 dir = targetPos - headPos;
            float len = dir.magnitude;
            if (len < 1e-4f) return default; // ゼロ距離: 正面扱い
            dir /= len;
            float fz = Vector3.Dot(dir, fwd);
            float fx = Vector3.Dot(dir, right);
            float fy = Vector3.Dot(dir, up);
            Angles a;
            a.Yaw = Mathf.Atan2(fx, fz) * Mathf.Rad2Deg;
            a.Pitch = Mathf.Asin(Mathf.Clamp(fy, -1f, 1f)) * Mathf.Rad2Deg;
            return a;
        }

        /// <summary>
        /// 1 フレームぶん状態を進め、適用すべき最終角（適用率込み）を返す。
        /// 全パラメータ dt 補正済（フレームレート非依存）。
        /// Tuning は runner が Configs の解決済み値で埋めて渡す（solver は BepInEx 非依存を維持）。
        /// </summary>
        public static StepResult Step(LookAtState s, Angles target, float dt, in Tuning t)
        {
            // 1) engage/release ヒステリシス（生のターゲット角で判定）
            bool inEngage = Mathf.Abs(target.Pitch) <= t.EngagePitchDeg
                         && Mathf.Abs(target.Yaw) <= t.EngageYawDeg;
            bool outRelease = Mathf.Abs(target.Pitch) > t.ReleasePitchDeg
                           || Mathf.Abs(target.Yaw) > t.ReleaseYawDeg;
            if (!s.Engaged && inEngage) s.Engaged = true;
            else if (s.Engaged && outRelease) s.Engaged = false;

            // 解除中は正面（0°）へ復帰
            float tgtYaw = s.Engaged ? target.Yaw : 0f;
            float tgtPitch = s.Engaged ? target.Pitch : 0f;

            // 2) 首デッドゾーン（小さなズレに反応しない・移動ヒステリシス）
            // Stop > Start の誤設定は Start に丸め、Moving が毎フレ toggle するジッタを防ぐ
            float dzStop = Mathf.Min(t.DeadZoneStopDeg, t.DeadZoneStartDeg);
            float err = Mathf.Max(Mathf.Abs(tgtYaw - s.HeadYaw),
                                  Mathf.Abs(tgtPitch - s.HeadPitch));
            if (!s.Moving && err >= t.DeadZoneStartDeg) s.Moving = true;
            else if (s.Moving && err <= dzStop) s.Moving = false;

            // 3) 首: dt 補正付き指数平滑（Moving 中のみ追従）
            if (s.Moving)
            {
                float k = 1f - Mathf.Exp(-dt / t.HeadTau);
                s.HeadYaw += (tgtYaw - s.HeadYaw) * k;
                s.HeadPitch += (tgtPitch - s.HeadPitch) * k;
            }

            // 4) 目: 速い吸着（デッドゾーンなし）。gate は無し（T0 ②: 目演技は no-op）
            float ek = 1f - Mathf.Exp(-dt / t.EyeTau);
            s.EyeYaw += (tgtYaw - s.EyeYaw) * ek;
            s.EyePitch += (tgtPitch - s.EyePitch) * ek;

            StepResult r;
            r.HeadYawApplied = s.HeadYaw * t.HeadRatio;
            r.HeadPitchApplied = s.HeadPitch * t.HeadRatio;
            r.EyeYawApplied = s.EyeYaw * t.EyeYawRatio;
            r.EyePitchApplied = s.EyePitch * t.EyePitchRatio;
            return r;
        }
    }
}
