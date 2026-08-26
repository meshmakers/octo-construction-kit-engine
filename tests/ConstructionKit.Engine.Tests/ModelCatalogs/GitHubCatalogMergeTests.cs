using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
/// Tests for the catalog-index merge functions used by the publish path's read-merge-write
/// upserts (AB#4872). The merges run on the content the GitHub API returned at write time and are
/// re-invoked after a SHA conflict, so they must preserve entries a concurrent build published in
/// between and must be idempotent (return null when there is nothing to write).
/// </summary>
public class GitHubCatalogMergeTests
{
    [Fact]
    public void MergeModelVersionsCatalog_PreservesConcurrentlyPublishedVersion()
    {
        // Simulates the AB#4872 CI race: another build published 1.0.5 between our read and a
        // SHA-conflicted write; the re-merge must keep 1.0.5 while adding 1.0.0.
        var concurrent =
            GitHubCatalog.MergeModelVersionsCatalog(null, new CkModelId("TestModel", "1.0.5"), "Test library");
        Assert.NotNull(concurrent);

        var merged =
            GitHubCatalog.MergeModelVersionsCatalog(concurrent, new CkModelId("TestModel", "1.0.0"), "Test library");

        Assert.NotNull(merged);
        Assert.Contains("ck-testmodel-1.0.5.json", merged);
        Assert.Contains("ck-testmodel-1.0.0.json", merged);
        // The latest version must stay the higher one, not the one merged last
        Assert.Contains("\"latestVersion\": \"1.0.5\"", merged);
    }

    [Fact]
    public void MergeModelVersionsCatalog_ReturnsNullWhenAlreadyUpToDate()
    {
        var existing =
            GitHubCatalog.MergeModelVersionsCatalog(null, new CkModelId("TestModel", "1.0.0"), "Test library");

        var second =
            GitHubCatalog.MergeModelVersionsCatalog(existing, new CkModelId("TestModel", "1.0.0"), "Test library");

        Assert.Null(second);
    }

    [Fact]
    public void MergeModelVersionsCatalog_UpdatesChangedDescription()
    {
        var existing =
            GitHubCatalog.MergeModelVersionsCatalog(null, new CkModelId("TestModel", "1.0.0"), "Old description");

        var merged =
            GitHubCatalog.MergeModelVersionsCatalog(existing, new CkModelId("TestModel", "1.0.0"), "New description");

        Assert.NotNull(merged);
        Assert.Contains("New description", merged);
    }

    [Fact]
    public void MergeRootCatalog_AddsModelOnceAndPreservesConcurrentEntries()
    {
        var concurrent = GitHubCatalog.MergeRootCatalog(null, new CkModelId("OtherModel", "1.0.0"));
        var merged = GitHubCatalog.MergeRootCatalog(concurrent, new CkModelId("TestModel", "1.0.0"));

        Assert.NotNull(merged);
        Assert.Contains("OtherModel", merged);
        Assert.Contains("TestModel", merged);

        // Already listed (any version of the same model) — nothing to write
        Assert.Null(GitHubCatalog.MergeRootCatalog(merged, new CkModelId("TestModel", "2.0.0")));
    }

    [Fact]
    public void MergeModelLibraryCatalog_AddsMajorVersionOncePreservingConcurrentMajors()
    {
        var concurrent = GitHubCatalog.MergeModelLibraryCatalog(null, new CkModelId("TestModel", "1.0.0"));
        var merged = GitHubCatalog.MergeModelLibraryCatalog(concurrent, new CkModelId("TestModel", "2.0.0"));

        Assert.NotNull(merged);
        Assert.Contains("ck-models/v2/t/TestModel/1/catalog.json", merged);
        Assert.Contains("ck-models/v2/t/TestModel/2/catalog.json", merged);

        // Same major already listed — nothing to write
        Assert.Null(GitHubCatalog.MergeModelLibraryCatalog(merged, new CkModelId("TestModel", "2.1.0")));
    }
}
