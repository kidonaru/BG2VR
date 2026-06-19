namespace BG2VR.DrinkGlass
{
    /// <summary>可視フラグ（Glass / Cocktail_1 / Cocktail_2）から複製するプロップを優先順
    /// （Glass > Cocktail1 > Cocktail2）で 1 つ選ぶ純ロジック（UnityEngine / ゲーム型 非依存）。
    /// VipRoomScene が glass/cocktail を 2 系統で扱うのに合わせ、両方可視なら Glass を優先する。</summary>
    public static class DrinkPropSelector
    {
        public static DrinkPropKind Select(bool glass, bool cocktail1, bool cocktail2)
        {
            if (glass) return DrinkPropKind.Glass;
            if (cocktail1) return DrinkPropKind.Cocktail1;
            if (cocktail2) return DrinkPropKind.Cocktail2;
            return DrinkPropKind.None;
        }
    }
}
