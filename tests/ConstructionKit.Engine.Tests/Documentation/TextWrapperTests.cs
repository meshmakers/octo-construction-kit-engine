using Meshmakers.Octo.ConstructionKit.Engine.Documentation;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Documentation;

public class TextWrapperTests
{
    [Fact]
    public void EscapeMdxSpecialCharacters_EscapesCurlyBraces()
    {
        var result = TextWrapper.EscapeMdxSpecialCharacters("value is {x}");

        Assert.Equal("value is \\{x\\}", result);
    }

    [Fact]
    public void EscapeMdxSpecialCharacters_EscapesAngleBracketsThatLookLikeJsxTags()
    {
        // Real failing input from System.Communication-3 Hostname description.
        var input = "https://<hostname>/<tenantId>";

        var result = TextWrapper.EscapeMdxSpecialCharacters(input);

        // Both `<` and `>` are escaped (`\<`, `\>`) for symmetry with the MDX/JSX tag pair.
        Assert.NotNull(result);
        Assert.Equal(@"https://\<hostname\>/\<tenantId\>", result);
        // Regression guard: no unescaped angle bracket should remain.
        Assert.DoesNotMatch(@"(?<!\\)[<>]", result);
    }

    [Fact]
    public void EscapeMdxSpecialCharacters_ReturnsNullForNull()
    {
        Assert.Null(TextWrapper.EscapeMdxSpecialCharacters(null));
    }

    [Fact]
    public void EscapeMdxSpecialCharacters_ReturnsEmptyForEmpty()
    {
        Assert.Equal(string.Empty, TextWrapper.EscapeMdxSpecialCharacters(string.Empty));
    }
}
