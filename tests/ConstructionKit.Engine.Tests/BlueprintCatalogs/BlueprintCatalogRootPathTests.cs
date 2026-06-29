using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Tests for relocating the local blueprint catalog via
/// <see cref="LocalFileSystemBlueprintCatalogOptions.ApplyRootPath" /> (the octo developer shell
/// ROOTPATH plumbing). The invariant under test mirrors the CK-model catalog contract: applying a
/// root path moves catalog content AND catalog cache together, so a writer and a reader configured
/// with the same root see each other's blueprints while a cache at a different root than the content
/// (split-brain) is structurally prevented.
/// </summary>
public class BlueprintCatalogRootPathTests
{
    [Fact]
    public void ApplyRootPath_SetsRootPathAndCacheDirectoryTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), "BlueprintCatalogRootPathTests", Guid.NewGuid().ToString(), "a");
        var options = new LocalFileSystemBlueprintCatalogOptions();

        options.ApplyRootPath(root);

        Assert.Equal(root, options.RootPath);
        Assert.Equal(Path.Combine(root, "cache"), options.CacheDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyRootPath_WithNullOrWhitespace_KeepsDefaultsUntouched(string? rootPath)
    {
        var options = new LocalFileSystemBlueprintCatalogOptions();
        var defaults = new LocalFileSystemBlueprintCatalogOptions();

        options.ApplyRootPath(rootPath);

        // No half-applied state: both values must remain at the defaults.
        Assert.Equal(defaults.RootPath, options.RootPath);
        Assert.Equal(defaults.CacheDirectory, options.CacheDirectory);
    }

    [Fact]
    public void ApplyRootPath_PreservesCase()
    {
        // Guards against the ConfigCommand-style .ToLower() bug — on case-sensitive file
        // systems a lower-cased path is a different directory (instant split-brain).
        var options = new LocalFileSystemBlueprintCatalogOptions();
        var mixedCasePath = Path.Combine(Path.GetTempPath(), "MixedCase", "SubDir");

        options.ApplyRootPath(mixedCasePath);

        Assert.Equal(mixedCasePath, options.RootPath);
    }

    [Fact]
    public void DefaultRootPath_StaysInUserProfile()
    {
        // Back-compat contract for consumers without a ROOTPATH override: no root path applied
        // means the catalog stays at ~/.octo/local-blueprint-catalog.
        var options = new LocalFileSystemBlueprintCatalogOptions();

        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".octo/local-blueprint-catalog");
        Assert.Equal(expected, options.RootPath);
    }
}
