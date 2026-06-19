using GB.Bar.MiniGame;
using HarmonyLib;
using UnityEngine;
using UnityVRMod.Core;
using BG2VR.VrInput;

namespace BG2VR.KarutaStick
{
    /// <summary>
    /// カルタ札取りの VR スティック対応（spec §4）。Karuta.getPlayerInput() を Postfix し、左右スティックが
    /// 札に解決したら（card!=None）スティック由来の札で __result を上書きする（AhhnVrPatches と同型＝VR
    /// 有効時のみゲームのミニゲームメソッドを patch し VRModCore 直読み）。getPlayerInput は受付ループ中・
    /// 非ポーズでのみ呼ばれる（Karuta.cs L560-574）ためゲートは自動成立＝probe 不要。
    /// IsKeyboardUsing/Switch 分岐を回避するため Karuta.Input を直接生成する（VR では device が物理
    /// mouse/keyboard 固定＝顔札がキーボード専用になる破綻を構造的に解消・spec §1）。
    /// 上書きは card!=None で行う（__result!=None でも上書き）。理由: 既存 VrGameButtonsRunner が VR
    /// 右スティックを nav 注入（→UpTriggered 等）するため、受付中の右スティックは dpad 節で十字札を
    /// 立てる。__result==None 限定だと右スティック↑が十字札 Up を返して顔札 Y へ到達不能になる。
    /// card!=None 上書きで「受付中の VR スティック入力を権威化」し、右スティック→顔札/左→十字札を両立
    /// （VrGameButtonsRunner/probe 無改変・spec §4.1「上書き条件」節）。中立(card==None)では __result 不変。
    /// </summary>
    [HarmonyPatch(typeof(Karuta), "getPlayerInput")]
    internal static class Karuta_getPlayerInput_Patch
    {
        // edge 検知の状態（カルタは同時 1 インスタンス＝static で十分）。StickNavMapper のリリース
        // ヒステリシスで stale 状態は自己治癒（ゼロ近傍を 1 度読めば held 解除）＝突入 reset 不要。
        private static readonly StickNavMapper s_left = new StickNavMapper();
        private static readonly StickNavMapper s_right = new StickNavMapper();

        private static void Postfix(ref Karuta.Input __result)
        {
            if (!Configs.KarutaStickEnabled.Value || !VRModCore.IsVrActive) return;

            VrControllerSnapshot l = VRModCore.GetControllerSnapshot(VrHand.Left);
            VrControllerSnapshot r = VRModCore.GetControllerSnapshot(VrHand.Right);
            float th = Configs.KarutaStickThreshold.Value;
            Vector2 lv = l.Valid ? l.Stick : Vector2.zero;
            Vector2 rv = r.Valid ? r.Stick : Vector2.zero;
            StickNavMapper.NavState ln = s_left.Update(lv, th);
            StickNavMapper.NavState rn = s_right.Update(rv, th);

            KarutaCard card = KarutaStickMap.Resolve(ln, lv, rn, rv);
            // スティックが札に解決したら上書き（nav 注入の dpad 結果も含め＝VR スティック入力を権威化）。
            // card==None（中立）では __result を触れず物理キーボード(i/j/k/l)/パッド入力を温存（spec §4.1）。
            if (card != KarutaCard.None)
                __result = ToInput(card);
        }

        private static Karuta.Input ToInput(KarutaCard card)
        {
            switch (card)
            {
                case KarutaCard.A: return Karuta.Input.A;
                case KarutaCard.B: return Karuta.Input.B;
                case KarutaCard.X: return Karuta.Input.X;
                case KarutaCard.Y: return Karuta.Input.Y;
                case KarutaCard.Up: return Karuta.Input.Up;
                case KarutaCard.Down: return Karuta.Input.Down;
                case KarutaCard.Left: return Karuta.Input.Left;
                case KarutaCard.Right: return Karuta.Input.Right;
                default: return Karuta.Input.None;
            }
        }
    }
}
