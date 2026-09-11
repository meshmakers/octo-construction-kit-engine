using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
///     Covers the resolution of the seed-data file list a blueprint declares (AB#4758). Runtime and
///     authoring tooling share this helper, so the two can never disagree about which files make up
///     a blueprint's seed.
/// </summary>
public class BlueprintSeedDataTests
{
    [Fact]
    public void ResolvePaths_NoSeedData_ReturnsEmpty()
    {
        var meta = new BlueprintMetaRootDto();

        Assert.Empty(BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public void ResolvePaths_SingleSeedDataPath_ReturnsThatPath()
    {
        var meta = new BlueprintMetaRootDto { SeedDataPath = "seed-data/entities.yaml" };

        Assert.Equal(["seed-data/entities.yaml"], BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public void ResolvePaths_SeedDataPaths_KeepsDeclarationOrder()
    {
        var meta = new BlueprintMetaRootDto
        {
            SeedDataPaths =
            [
                "seed-data/identity/roles.yaml",
                "seed-data/configurations/base.yaml",
                "seed-data/data-flows/camt053.yaml"
            ]
        };

        Assert.Equal([
            "seed-data/identity/roles.yaml",
            "seed-data/configurations/base.yaml",
            "seed-data/data-flows/camt053.yaml"
        ], BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public void ResolvePaths_BothForms_LoadsSinglePathFirstAndKeepsBoth()
    {
        // Both properties are honoured rather than one silently winning: dropping either would
        // drop entities from the install without any signal.
        var meta = new BlueprintMetaRootDto
        {
            SeedDataPath = "seed-data/entities.yaml",
            SeedDataPaths = ["seed-data/data-flows/camt053.yaml"]
        };

        Assert.Equal(["seed-data/entities.yaml", "seed-data/data-flows/camt053.yaml"],
            BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public void ResolvePaths_DuplicatePath_IsCollapsedOnce()
    {
        // Same file listed twice (e.g. also named by seedDataPath) must not be imported twice —
        // the duplicate-rtId guard would otherwise reject the blueprint's own file against itself.
        var meta = new BlueprintMetaRootDto
        {
            SeedDataPath = "seed-data/entities.yaml",
            SeedDataPaths = ["/seed-data/entities.yaml", "seed-data\\entities.yaml"]
        };

        Assert.Equal(["seed-data/entities.yaml"], BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public void ResolvePaths_BlankEntries_AreDropped()
    {
        var meta = new BlueprintMetaRootDto
        {
            SeedDataPaths = ["seed-data/a.yaml", "", "   ", "seed-data/b.yaml"]
        };

        Assert.Equal(["seed-data/a.yaml", "seed-data/b.yaml"], BlueprintSeedData.ResolvePaths(meta));
    }

    [Fact]
    public void ResolvePaths_BackslashesAndLeadingSlash_AreNormalised()
    {
        var meta = new BlueprintMetaRootDto { SeedDataPaths = ["\\seed-data\\pipelines\\camt053.yaml"] };

        Assert.Equal(["seed-data/pipelines/camt053.yaml"], BlueprintSeedData.ResolvePaths(meta));
    }
}
