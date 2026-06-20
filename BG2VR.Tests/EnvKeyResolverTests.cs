using Xunit;
using BG2VR.ScenePinned;

public class EnvKeyResolverTests
{
    // --- 非ミニゲーム（miniGameName = null）: 従来挙動 ---
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SteelFrameScene")]
    public void 非ミニゲームで保存対象外はnullを返す(string typeName)
    {
        Assert.Null(EnvKeyResolver.ResolveKey(typeName, null));
    }

    [Theory]
    [InlineData("HoleScene")]
    [InlineData("VipRoomScene")]
    [InlineData("GameRoomScene")]
    [InlineData("Talk2DScene")]
    public void 非ミニゲームは型名をそのままキーにする(string typeName)
    {
        Assert.Equal(typeName, EnvKeyResolver.ResolveKey(typeName, null));
    }

    // --- ミニゲーム進行中: env + 種別の複合キー ---
    [Theory]
    [InlineData("GameRoomScene", "TWISTER", "GameRoomScene.TWISTER")]
    [InlineData("GameRoomScene", "KARUTA", "GameRoomScene.KARUTA")]
    [InlineData("HoleScene", "AHHN_GAME", "HoleScene.AHHN_GAME")]
    [InlineData("VipRoomScene", "KARAOKE", "VipRoomScene.KARAOKE")]
    [InlineData("SteelFrameScene", "STEEL_FRAME", "SteelFrameScene.STEEL_FRAME")]
    public void ミニゲーム進行中は複合キーを返す(string env, string mg, string expected)
    {
        // SteelFrame は従来除外だったが、ミニゲーム枠で新規 pinnable になる点を含めて確認。
        Assert.Equal(expected, EnvKeyResolver.ResolveKey(env, mg));
    }

    // --- 退化ケース: 番兵/空のミニゲーム名は非ミニゲーム扱い ---
    [Theory]
    [InlineData("NONE")]
    [InlineData("NUM")]
    [InlineData("")]
    public void 番兵や空のミニゲーム名は非ミニゲーム扱い(string mg)
    {
        Assert.Equal("GameRoomScene", EnvKeyResolver.ResolveKey("GameRoomScene", mg));
    }

    [Fact]
    public void env不在ならミニゲーム中でもnull()
    {
        Assert.Null(EnvKeyResolver.ResolveKey(null, "TWISTER"));
    }
}
