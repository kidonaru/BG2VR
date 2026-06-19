using UnityEngine;

namespace BG2VR.AhhnVr
{
    /// <summary>あ〜ん VR の当たり判定（純関数・テスト可能）。
    /// food 先端とターゲット（ForCast=キャストの口 pos_mouth / ForPlayer=HMD 顔）の
    /// 3D ワールド距離でしきい判定する（スクリーン空間ではなく VR ネイティブ）。</summary>
    internal static class AhhnEatJudge
    {
        /// <summary>a と b のワールド距離が threshold 以内なら true（境界 == は成功扱い）。</summary>
        public static bool Hit(Vector3 a, Vector3 b, float threshold)
        {
            return (a - b).magnitude <= threshold;
        }

        /// <summary>bool 入力の rising edge（false→true）を検出し prev を cur で更新する。
        /// 押しっぱなし中は 2 度目以降 false、離して再押下で再発火。トリガー前フレーム値を ref で渡す。</summary>
        public static bool RisingEdge(ref bool prev, bool cur)
        {
            bool rising = cur && !prev;
            prev = cur;
            return rising;
        }
    }
}
