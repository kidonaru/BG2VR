using System.Collections.Generic;
using GB;
using GB.Scene;
using UnityEngine;

namespace BG2VR.DrinkGlass
{
    /// <summary>現在選択中のキャスト（GetCurrentCast）の CharacterHandle を引き、グラス/カクテルプロップを
    /// 手に持っていれば複製元 SkinnedMeshRenderer を返す probe（ゲーム型依存＝テスト対象外。判定の優先順は
    /// 純ロジック DrinkPropSelector）。publicize 参照で CharacterHandle.m_props を直読みする
    /// （MiniGameProbe が m_state を直読みするのと同方式）。同伴/Bar で複数キャラが active でも
    /// 選択中キャストのグラスだけを対象にする（全キャラ走査の先勝ちは不採用）。
    /// 毎フレ呼ばれるため重い API（GetComponentsInChildren 等）は足さない＝軽量解決に留める。</summary>
    internal static class DrinkGlassProbe
    {
        /// <summary>選択中キャストが手持ちグラスを持っていれば true ＋ 複製元 SMR と種別を返す。無ければ false。</summary>
        public static bool TryGetSource(out SkinnedMeshRenderer source, out CharacterHandle.Props kind)
        {
            source = null;
            kind = default;

            var sys = GBSystem.Instance;
            var env = sys != null ? sys.GetActiveEnvScene() : null;
            List<CharacterHandle> chars = env != null ? env.m_characters : null;
            if (chars == null) return false;

            // 現在選択中のキャストのみ対象。GetCharID で一致するハンドルを引く（EnvSceneBase が
            // PlayCharacterMotion 等で使うのと同じ照合）。不在なら何も出さない。
            var cast = sys.RefGameData().GetCurrentCast();
            CharacterHandle h = chars.Find(x => x != null && x.GetCharID() == cast);
            if (h == null) return false;

            List<SkinnedMeshRenderer> props = h.m_props; // publicize 直読み（実体は List<SkinnedMeshRenderer>）
            if (props == null) return false;

            DrinkPropKind picked = DrinkPropSelector.Select(
                Visible(props, (int)CharacterHandle.Props.Glass),
                Visible(props, (int)CharacterHandle.Props.Cocktail_1),
                Visible(props, (int)CharacterHandle.Props.Cocktail_2));
            if (picked == DrinkPropKind.None) return false;

            kind = ToProps(picked);
            source = props[(int)kind]; // Visible で非 null 確認済み
            return true;
        }

        // m_props[idx] が存在・非 null・activeInHierarchy のとき可視。
        // activeSelf でなく activeInHierarchy を見るのが要点: prop の可視は active キャラの
        // updatePropsVisibility() が毎フレ motion gate で SetActive する（DRINK 中のみ Glass を active）。
        // しかし非アクティブな保持キャラ（Home/Menu に残る Hole 等の clone）は Update が回らず
        // prop の activeSelf が stale な true のまま＝activeSelf 判定だとキャラ不在シーンで誤検出する
        // （実機: HomeScene で env=HoleScene 保持・charaGO 非アクティブ・Glass self=True/inHier=False を確認）。
        // activeInHierarchy は祖先（キャラ GO）の非アクティブを含むため、キャラが active かつ DRINK 中の
        // ときだけ true になり全シーンで正しく判定できる。
        private static bool Visible(List<SkinnedMeshRenderer> props, int idx)
            => idx < props.Count && props[idx] != null && props[idx].gameObject.activeInHierarchy;

        private static CharacterHandle.Props ToProps(DrinkPropKind k)
            => k == DrinkPropKind.Glass ? CharacterHandle.Props.Glass
             : k == DrinkPropKind.Cocktail1 ? CharacterHandle.Props.Cocktail_1
             : CharacterHandle.Props.Cocktail_2;
    }
}
