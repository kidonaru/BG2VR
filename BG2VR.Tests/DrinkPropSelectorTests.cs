using Xunit;
using BG2VR.DrinkGlass;

public class DrinkPropSelectorTests
{
    [Fact]
    public void None_WhenNothingVisible()
        => Assert.Equal(DrinkPropKind.None, DrinkPropSelector.Select(false, false, false));

    [Fact]
    public void Glass_HasTopPriority()
        => Assert.Equal(DrinkPropKind.Glass, DrinkPropSelector.Select(true, true, true));

    [Fact]
    public void Cocktail1_WhenNoGlass()
        => Assert.Equal(DrinkPropKind.Cocktail1, DrinkPropSelector.Select(false, true, true));

    [Fact]
    public void Cocktail2_WhenOnlyIt()
        => Assert.Equal(DrinkPropKind.Cocktail2, DrinkPropSelector.Select(false, false, true));
}
