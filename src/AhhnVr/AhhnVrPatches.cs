using System.Threading;
using Cysharp.Threading.Tasks;
using GB;
using GB.Bar.MiniGame;
using HarmonyLib;
using UnityEngine;
using UnityVRMod.Core;
using BG2VR.VrInput; // ControllerModelPose

namespace BG2VR.AhhnVr
{
    /// <summary>トリガー rising edge の前フレーム値（ForCast/ForPlayer 各 1 個）。
    /// AhhnGame は同時 1 インスタンス＝static で十分。AhhnModeBase.Setup Postfix で
    /// ミニゲーム突入ごとに true へリセット（突入時に押しっぱなしのトリガー＝UI クリックの押下持ち越しで
    /// 初回フレーム誤発火するのを防ぐ。true 起点なら一度離して再押下するまで rising しない）。</summary>
    internal static class AhhnVrState
    {
        public static bool PrevTriggerCast = true;
        public static bool PrevTriggerPlayer = true;
    }

    /// <summary>ミニゲーム突入（mode.Setup）ごとにトリガー edge 状態をリセット（押しっぱなし誤発火防止）。</summary>
    [HarmonyPatch(typeof(AhhnModeBase), "Setup")]
    internal static class AhhnModeBase_Setup_Patch
    {
        private static void Postfix()
        {
            AhhnVrState.PrevTriggerCast = true;
            AhhnVrState.PrevTriggerPlayer = true;
        }
    }

    /// <summary>
    /// あ〜ん「食べさせる」側（ForCastMode）の VR 操作化（spec 2026-06-14）。VR 有効時のみ Update を
    /// Prefix で全置換し、food を右手 pose に追従させ右トリガーで判定する（成功演出/好感度/SE/遷移は
    /// publicize 経由でゲーム私有メソッドを流用）。非 VR / AhhnVrEnabled=false は原 Update を素通し。
    /// 咀嚼中の二重起動・tween 競合が無い理由: 命中で m_state=EATING / ミスで eatMiss が
    /// m_state=EAT_MISS をセットし（どちらも完了時 State.NONE へ復帰・原実装確認済み）、以降のフレームは
    /// 下の m_state!=NONE ガードで food 追従も判定も止まる＝DOMove tween と world 直書きが競合しない
    /// （同フレームは「直書き→tween 起動→return」で tween が以降を所有する）。
    /// </summary>
    [HarmonyPatch(typeof(ForCastMode), "Update")]
    internal static class ForCastMode_Update_Patch
    {
        private static bool Prefix(ForCastMode __instance, CancellationToken cts)
        {
            if (!Configs.AhhnVrEnabled.Value || !VRModCore.IsVrActive) return true; // 原ロジック（ゲームパッド）

            VrControllerSnapshot snap = VRModCore.GetControllerSnapshot(VrHand.Right);
            bool trig = snap.Valid && snap.Trigger;
            // state 早期 return より前に prev を更新（咀嚼中の押下持ち越しで誤発火しない）。
            bool rising = AhhnEatJudge.RisingEdge(ref AhhnVrState.PrevTriggerCast, trig);

            // 咀嚼/ミス tween 中（NONE 以外）は原同様ノータッチ（食べ物移動 tween を阻害しない）。
            if (__instance.m_state != AhhnModeBase.State.NONE) return false;

            Transform rig = VRModCore.GetRigTransform();
            if (rig == null || !snap.Valid)
            {
                __instance.countTimer(cts); // 入力できなくても時間は進める
                return false;
            }

            // 右手 world pose → food world pose（手元 local オフセットを乗せる。ControllerModelPose を再利用＝
            // 式 localPos=snapPos+snapRot*posOffset は frame 非依存のため、world 入力でそのまま world 出力になる）。
            Vector3 handWorldPos = rig.TransformPoint(snap.RigLocalPosition);
            Quaternion handWorldRot = rig.rotation * snap.RigLocalRotation;
            Vector3 posOffset = new Vector3(
                Configs.AhhnFoodHandPosOffsetX.Value,
                Configs.AhhnFoodHandPosOffsetY.Value,
                Configs.AhhnFoodHandPosOffsetZ.Value);
            Quaternion rotOffset = Quaternion.Euler(
                Configs.AhhnFoodHandRotOffsetX.Value,
                Configs.AhhnFoodHandRotOffsetY.Value,
                Configs.AhhnFoodHandRotOffsetZ.Value);
            ControllerModelPose.Compute(handWorldPos, handWorldRot, posOffset, rotOffset,
                out Vector3 foodPos, out Quaternion foodRot);
            __instance.m_food.transform.position = foodPos; // 親(カメラ親)に関係なく world 上書き
            __instance.m_food.transform.rotation = foodRot;

            if (rising)
            {
                // 原 Update の押下時副作用を再現（eat 演出の起点）。eatSuccess は内部で Ahhn_Bite SE +
                // PlayVoice + m_foodList[m_eatCount-1] 非表示を行う（Ahhn_Success との二重 SE は原仕様＝忠実）。
                __instance.m_prevPos = __instance.m_food.transform.position;
                __instance.moveFood(__instance.m_food.transform.position + __instance.m_food.transform.up * 0.05f, 0.4f);
                GBSystem.Instance.PlaySE(SoundManager.SE.Ahhn_Success);
                if (AhhnEatJudge.Hit(__instance.m_foodTip.position, __instance.m_mouth.position,
                        Configs.AhhnForCastEatDistance.Value))
                {
                    __instance.m_eatCount++;
                    __instance.m_state = AhhnModeBase.State.EATING;
                    if (__instance.m_eatCount >= 3)
                    {
                        GBSystem.Instance.PlaySE(SoundManager.SE.Minigame_End);
                        __instance.eatSuccess();
                        __instance.finishGame(cts).Forget();
                    }
                    else
                    {
                        __instance.eatSuccess();
                        __instance.nextGame(cts).Forget();
                    }
                }
                else
                {
                    __instance.eatMiss(cts).Forget(); // 内部で m_state=EAT_MISS → moveFood(m_prevPos) → State.NONE
                }
            }
            __instance.countTimer(cts);
            return false;
        }
    }

    /// <summary>
    /// あ〜ん「食べる」側（ForPlayerMode）の VR 操作化。VR 有効時のみ Update を Prefix で全置換。
    /// food はキャスト側のまま（差し出し演出 playRandomCharacterMotion を維持）。プレイヤーは
    /// 頭(HMD)を寄せ、いずれかのトリガーで判定する。2D 口カーソル(MouthObj)は VR では使わないため非表示。
    /// </summary>
    [HarmonyPatch(typeof(ForPlayerMode), "Update")]
    internal static class ForPlayerMode_Update_Patch
    {
        private static bool Prefix(ForPlayerMode __instance, CancellationToken cts)
        {
            if (!Configs.AhhnVrEnabled.Value || !VRModCore.IsVrActive) return true;

            VrControllerSnapshot l = VRModCore.GetControllerSnapshot(VrHand.Left);
            VrControllerSnapshot r = VRModCore.GetControllerSnapshot(VrHand.Right);
            bool trig = (l.Valid && l.Trigger) || (r.Valid && r.Trigger);
            bool rising = AhhnEatJudge.RisingEdge(ref AhhnVrState.PrevTriggerPlayer, trig);

            if (__instance.m_state != AhhnModeBase.State.NONE) return false;

            // VR では 2D 口カーソルを使わず HMD 顔位置で判定（spec §5）。active のときだけ非表示にする＝冪等。
            // finishGame/Release/timeUp も MouthObj を SetActive(false) するが、この per-frame 非表示は
            // NONE 中だけ走り（非 NONE では touch しない）冪等なので競合しない。Eat() アニメは VR では呼ばない。
            if (__instance.m_ui.MouthObj.Parent.activeSelf) __instance.m_ui.MouthObj.SetActive(false);

            Camera eyeCam = VRModCore.GetVrEyeCamera();
            if (eyeCam == null)
            {
                __instance.countTimer(cts);
                return false;
            }

            __instance.playRandomCharacterMotion(); // 原と同じ差し出し演出（内部に MOTION_INTERVAL タイマ）

            if (rising)
            {
                if (AhhnEatJudge.Hit(__instance.m_foodTip.position, eyeCam.transform.position,
                        Configs.AhhnForPlayerEatDistance.Value))
                {
                    GBSystem.Instance.PlaySE(SoundManager.SE.Ahhn_Bite);
                    __instance.m_foodList[__instance.m_eatCount].SetActive(false);
                    __instance.m_eatCount++;
                    __instance.m_state = AhhnModeBase.State.EATING;
                    if (__instance.m_eatCount >= 3) __instance.finishGame(cts).Forget();
                    else __instance.nextGame(cts).Forget();
                    return false; // 原 ForPlayerMode.Update は成功時 early return（ForPlayerMode.cs L153）で
                                  // 末尾 countTimer をスキップする。これを忠実に再現＝buzzer 際(t≥20s)で
                                  // 成功と TIME_UP が二重遷移するのを防ぐ。ミス分岐は原同様 countTimer へ落とす。
                }
                else
                {
                    GBSystem.Instance.PlaySE(SoundManager.SE.Ahhn_Bite); // 原は miss でも Ahhn_Bite を鳴らす
                }
            }
            __instance.countTimer(cts);
            return false;
        }
    }
}
