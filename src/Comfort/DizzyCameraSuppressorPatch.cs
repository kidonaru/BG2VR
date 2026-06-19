using GB.Scene;
using HarmonyLib;
using UnityVRMod.Core;

namespace BG2VR.Comfort
{
    /// <summary>
    /// VR 中、プレイヤー酩酊時の酔いカメラ回転を抑制する。
    ///
    /// EnvSceneBase.GBUpdate() は s_dizzyCameraOnPlayerDrunk > 0 のとき env カメラの
    /// localEulerAngles を sin 回転で毎フレ上書きする（EnvSceneBase.cs:401-419）。VR rig は
    /// この env カメラに親付けされるため回転が rig ごと適用され強い酔いを生む。
    ///
    /// Prefix で同一呼出内に static フィールドを 0 にしておくと、original が 0 を読み
    /// if (s_dizzy > 0f) を抜けてカメラ書込が一切走らない。DOTween の更新順に依存しない
    /// （EnableDizzyCameraOnPlayerDrunk の走行中 tween 問題を回避）。
    ///
    /// dizzy の適用はこの EnvSceneBase.GBUpdate 唯一。EnvSceneBase 派生（HoleScene/GameRoomScene/
    /// Talk2DScene/SteelFrameScene/KaraokeTestScene）は GBUpdate を override せず本メソッドを直接
    /// 使用、唯一 override する VipRoomScene も先頭で base.GBUpdate() を呼ぶ → 全 env scene で発火
    /// （2026-06-02 検証）。BarScene は GBBehaviour 直系で EnvSceneBase 派生ではなく dizzy のトリガ
    /// のみ呼ぶ（カメラ回転は適用しない）。
    ///
    /// s_dizzyCameraOnPlayerDrunk は private static だが Assembly-CSharp は Publicize="true"
    /// 参照のため直接代入できる。
    /// </summary>
    [HarmonyPatch(typeof(EnvSceneBase), "GBUpdate")]
    public static class DizzyCameraSuppressorPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (VRModCore.IsVrActive && Configs.SuppressDizzyCamera.Value)
            {
                EnvSceneBase.s_dizzyCameraOnPlayerDrunk = 0f;
            }
        }
    }
}
