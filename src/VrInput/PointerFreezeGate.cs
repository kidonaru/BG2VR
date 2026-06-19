using System;

namespace BG2VR.VrInput
{
    /// <summary>
    /// トリガーのアナログ押し込み開始(onset)を検知してカーソル凍結区間を判定する状態機械
    /// （System のみ依存・純ロジック）。onset はクリック確定（fork hysteresis press≥0.7）より
    /// 先に来るため、click 時点では ray が狙った位置で凍結済みになる＝握り込みによるずれを防ぐ。
    /// しきい値は Configs の解決済み値を引数で受ける（CLAUDE.md 規約・テスト容易性）。
    /// 解除（release / timeout）後は recoverSec の smoothstep で現照準へブレンド復帰（ワープしない）。
    /// </summary>
    public sealed class PointerFreezeGate
    {
        private enum State { Idle, Frozen, Recovering }
        private State m_state = State.Idle;
        private float m_frozenTime;
        private float m_recoverTime;
        // タイムアウト解除後、release 以下に戻るまで再凍結を禁止（押しっぱなしのチャタり防止）。
        private bool m_disarmed;

        public struct Result
        {
            public bool Frozen;     // 凍結中（Blend=0）
            public bool JustFroze;  // このフレームで凍結開始（呼び出し側が ray を latch する）
            public float Blend;     // 0=凍結 ray / 1=live ray / (0,1)=復帰中の smoothstep ブレンド
        }

        public Result Update(float triggerValue, float dt, float onset, float release, float timeoutSec, float recoverSec)
        {
            // release > onset の設定ミスは onset に丸める（凍結→即解除→再凍結のチャタり防止）。
            if (release > onset) release = onset;
            // disarm 解除は状態に依らず「一度 release 以下に戻った」時点。
            if (m_disarmed && triggerValue <= release) m_disarmed = false;

            switch (m_state)
            {
                case State.Idle:
                    if (!m_disarmed && triggerValue >= onset)
                    {
                        m_state = State.Frozen;
                        m_frozenTime = 0f;
                        return new Result { Frozen = true, JustFroze = true, Blend = 0f };
                    }
                    return new Result { Blend = 1f };

                case State.Frozen:
                    if (triggerValue <= release)
                    {
                        m_state = State.Recovering;
                        m_recoverTime = 0f;
                        return TickRecover(dt, recoverSec);
                    }
                    m_frozenTime += dt;
                    if (timeoutSec > 0f && m_frozenTime >= timeoutSec)
                    {
                        m_disarmed = true;
                        m_state = State.Recovering;
                        m_recoverTime = 0f;
                        return TickRecover(dt, recoverSec);
                    }
                    return new Result { Frozen = true, Blend = 0f };

                case State.Recovering:
                default:
                    if (!m_disarmed && triggerValue >= onset)
                    {
                        // 復帰途中の再押し込み＝即再凍結（呼び出し側は live を latch。微小ズレは許容）。
                        m_state = State.Frozen;
                        m_frozenTime = 0f;
                        return new Result { Frozen = true, JustFroze = true, Blend = 0f };
                    }
                    return TickRecover(dt, recoverSec);
            }
        }

        // 復帰ブレンドを dt 分進める。recoverSec<=0 は即時復帰（旧ワープ挙動）。
        private Result TickRecover(float dt, float recoverSec)
        {
            m_recoverTime += dt;
            float w = recoverSec <= 0f ? 1f : Math.Min(1f, m_recoverTime / recoverSec);
            if (w >= 1f)
            {
                m_state = State.Idle;
                return new Result { Blend = 1f };
            }
            float s = w * w * (3f - 2f * w); // smoothstep（出だしと終わりが滑らか）
            return new Result { Blend = s };
        }

        public void Reset()
        {
            m_state = State.Idle;
            m_disarmed = false;
        }
    }
}
