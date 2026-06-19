using UnityEngine;

namespace BG2VR.VrInput
{
    /// <summary>コントローラモデルの最終 rig-local pose を算出する純関数（テスト可能）。
    /// snapshot の grip pose に Meta FBX 原点 ⇄ OpenXR grip 原点 の整合オフセットを乗せる。
    /// レーザーピッチは適用しない（実機コントローラと 1:1）。
    /// 回転オフセットは **Quaternion で受け取る**（euler→Quaternion 変換 = `Quaternion.Euler` は native ECall で
    /// テスト host から呼べないため、解決済み Quaternion を Runner 側で渡す＝CLAUDE.md「純関数は解決済み値を受ける」）。
    /// この関数自体は Quaternion 乗算 + Vector3 回転のみ＝全 managed でテスト可能。</summary>
    internal static class ControllerModelPose
    {
        /// <param name="snapPos">snapshot.RigLocalPosition（grip）</param>
        /// <param name="snapRot">snapshot.RigLocalRotation（grip）</param>
        /// <param name="posOffset">位置オフセット（手のローカル軸基準・m）</param>
        /// <param name="rotOffset">回転オフセット（解決済み Quaternion・Runner が Quaternion.Euler で生成）</param>
        public static void Compute(Vector3 snapPos, Quaternion snapRot,
            Vector3 posOffset, Quaternion rotOffset,
            out Vector3 localPos, out Quaternion localRot)
        {
            localRot = snapRot * rotOffset;
            localPos = snapPos + snapRot * posOffset; // オフセットを手の向きに乗せる
        }

        /// <summary>手モデルの最終 localScale を算出する純関数。
        /// 手は片手 FBX を mirror して対の手を作るため、mirror 側は X を反転する（negative X scale）。
        /// scale = config 倍率、baseScale = アセット実寸補正の構造定数。
        /// scale と mirror 符号を 1 か所で確定させ、Runner 側で scale 代入後に mirror を上書きする順序事故を防ぐ。</summary>
        /// <param name="scale">config の拡大率（HandModelScale）</param>
        /// <param name="baseScale">アセット base scale（手=import 正規化前提で 1.0 / コントローラ=0.01）</param>
        /// <param name="mirror">true なら X 反転（mirror 側の手）</param>
        public static Vector3 HandModelScaleVector(float scale, float baseScale, bool mirror)
        {
            float m = scale * baseScale;
            return new Vector3(mirror ? -m : m, m, m);
        }

        /// <summary>回転を X 平面（local X 反転）で鏡映した Quaternion を返す純関数。
        /// 片手 FBX を negative X scale で mirror した対の手に**同一の回転オフセットを対称適用**するため
        /// （これをしないと mirror 側だけ回転が鏡映されず逆を向く＝実機で左右の向きが食い違う）。
        /// X 平面反射の rotation は (x, -y, -z, w)＝y,z 成分の符号反転。Quaternion 構築のみ＝managed でテスト可。</summary>
        public static Quaternion MirrorRotationX(Quaternion q)
        {
            return new Quaternion(q.x, -q.y, -q.z, q.w);
        }

        /// <summary>base 色（unlit tint）に明るさ倍率を乗じる純関数。RGB のみ乗算しアルファは保つ（不透明維持）。
        /// 倍率は **base に対し** 乗算する（_Color へ直接乗じない）ため毎フレ base×b を書いても累積しない＝冪等。
        /// 手元モデルは post-process 反映層にあり明るいと Bloom が乗るので、b を下げてグローを抑える用途。
        /// Color 演算は managed のみ＝テスト host で実行可（ECall なし）。</summary>
        public static Color Brightened(Color baseColor, float brightness)
            => new Color(baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
    }
}
