using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

public class BlueprintRelativePathTests
{
    [Theory]
    [InlineData("seed-data/entities.yaml", "seed-data/entities.yaml")]
    [InlineData("seed-data\\entities.yaml", "seed-data/entities.yaml")]
    [InlineData("migrations/from-1.0.0.yaml", "migrations/from-1.0.0.yaml")]
    [InlineData("a.yaml", "a.yaml")]
    public void Validate_AcceptsAndNormalises(string input, string expected)
    {
        var result = BlueprintRelativePath.Validate(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Validate_RejectsLeadingSlash()
    {
        // Leading-slash inputs are platform-rooted on Unix; the validator rejects them so that
        // a Linux-targeted call site doesn't accidentally escape the blueprint root via "/etc/...".
        Assert.Throws<BlueprintCatalogException>(() =>
            BlueprintRelativePath.Validate("/seed-data/entities.yaml"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("../etc/passwd")]
    [InlineData("seed/../escape.yaml")]
    [InlineData("./entities.yaml")]
    public void Validate_RejectsTraversalEmptyAndDotPaths(string input)
    {
        Assert.Throws<BlueprintCatalogException>(() => BlueprintRelativePath.Validate(input));
    }

    [Fact]
    public void Validate_NullInput_Throws()
    {
        Assert.Throws<BlueprintCatalogException>(() => BlueprintRelativePath.Validate(null!));
    }
}
