using BG2VR.VrInput;
using Xunit;

namespace BG2VR.Tests;

public class HandSkinShaderClassifierTests
{
    [Theory]
    [InlineData("Toon", true)]
    [InlineData("Standard", true)]
    [InlineData("UI/Default", true)]
    [InlineData("Hidden/InternalErrorShader", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAcceptable_RejectsPlaceholderAndEmpty(string shaderName, bool expected)
    {
        Assert.Equal(expected, HandSkinShaderClassifier.IsAcceptable(shaderName));
    }
}
