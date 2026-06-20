namespace BG2VR.DrinkGlass
{
    /// <summary>複製対象のドリンクプロップ種別（純ロジック用・ゲーム enum 非依存＝テスト host で扱える）。
    /// DrinkGlassProbe が CharacterHandle.Props（Glass/Cocktail_1/Cocktail_2）へ写像する。</summary>
    public enum DrinkPropKind
    {
        None = 0,
        Glass = 1,
        Cocktail1 = 2,
        Cocktail2 = 3,
    }
}
