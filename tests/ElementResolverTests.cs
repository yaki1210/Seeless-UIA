using SeelessUIA.Element;
using Xunit;

namespace SeelessUIA.Tests;

public class ElementResolverTests
{
    [Theory]
    [InlineData("@e1", "e1")]
    [InlineData("ref=e42", "e42")]
    [InlineData("e99", "e99")]
    [InlineData("e1", "e1")]
    public void ParseRef_ParsesCorrectly(string input, string expected)
    {
        var result = ElementResolver.ParseRef(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("button")]
    [InlineData("e")]
    [InlineData("1")]
    [InlineData("@button")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseRef_ReturnsNullForInvalid(string? input)
    {
        var result = ElementResolver.ParseRef(input!);
        Assert.Null(result);
    }

    [Fact]
    public void ParseRef_AcceptsLargeRefNumbers()
    {
        var result = ElementResolver.ParseRef("e123456");
        Assert.Equal("e123456", result);
    }
}
