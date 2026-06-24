using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Round-trip tests for <see cref="GitHubBlueprintCatalog" /> unpublish. The real catalog runs against a
/// stateful in-memory <see cref="IGitHubClientWrapper" /> (<see cref="InMemoryGitHubClientWrapper" />) so the
/// three-layer index cascade is exercised end to end without ever touching the real GitHub repository.
/// </summary>
public sealed class GitHubBlueprintCatalogUnpublishTests : IDisposable
{
    private const string BlueprintName = "MyBlueprint";

    private readonly InMemoryGitHubClientWrapper _git = new();
    private readonly string _tempDir;
    private readonly PrivateGitHubBlueprintCatalog _catalog;

    public GitHubBlueprintCatalogUnpublishTests()
    {
        var serializer = A.Fake<IBlueprintSerializer>();
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        var httpClientWrapper = A.Fake<IHttpClientWrapper>();
        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();

        _tempDir = Path.Combine(Path.GetTempPath(), nameof(GitHubBlueprintCatalogUnpublishTests), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var options = new PrivateGitHubBlueprintCatalogOptions
        {
            CacheDirectory = _tempDir,
            GitHubApiToken = "test-token"
        };

        A.CallTo(() => httpClientFactory.CreateClient(A<Uri>._)).Returns(httpClientWrapper);
        A.CallTo(() => gitHubClientFactory.CreateClient(A<IGitHubOptions>._)).Returns(_git);

        _catalog = new PrivateGitHubBlueprintCatalog(serializer, httpClientFactory, gitHubClientFactory,
            Options.Create(options));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task UnpublishAsync_RemovesOneVersion_KeepsSibling_AndPrunesVersionEntry()
    {
        await PublishVersionAsync("1.0.0");
        await PublishVersionAsync("1.1.0");

        // Both versions present before.
        Assert.True(_git.Has(BlueprintFilePath("1.0.0")));
        Assert.True(_git.Has(BlueprintFilePath("1.1.0")));

        await _catalog.UnpublishAsync(new BlueprintId(BlueprintName, "1.0.0"));

        // 1.0.0 files gone, 1.1.0 untouched.
        Assert.False(_git.Has(BlueprintFilePath("1.0.0")));
        Assert.False(_git.Has(SeedFilePath("1.0.0")));
        Assert.True(_git.Has(BlueprintFilePath("1.1.0")));

        // Version catalog still exists, no longer references 1.0.0 but still references 1.1.0.
        var versionsCatalog = _git.Content(VersionsCatalogPath(1));
        Assert.NotNull(versionsCatalog);
        Assert.DoesNotContain("1.0.0", versionsCatalog);
        Assert.Contains("1.1.0", versionsCatalog);

        // Library + root index still list the blueprint.
        Assert.True(_git.Has(LibraryCatalogPath()));
        Assert.Contains(BlueprintName, _git.Content(RootCatalogPath())!);
    }

    [Fact]
    public async Task UnpublishAsync_LastVersion_CascadesAwayMajorLibraryAndRootEntries()
    {
        await PublishVersionAsync("1.0.0");

        await _catalog.UnpublishAsync(new BlueprintId(BlueprintName, "1.0.0"));

        // All files and the version + library catalogs are gone.
        Assert.False(_git.Has(BlueprintFilePath("1.0.0")));
        Assert.False(_git.Has(VersionsCatalogPath(1)));
        Assert.False(_git.Has(LibraryCatalogPath()));

        // Root catalog still exists but no longer lists the blueprint.
        Assert.True(_git.Has(RootCatalogPath()));
        Assert.DoesNotContain(BlueprintName, _git.Content(RootCatalogPath())!);
    }

    [Fact]
    public async Task UnpublishAllVersionsAsync_RemovesEverythingForTheBlueprint()
    {
        await PublishVersionAsync("1.0.0");
        await PublishVersionAsync("1.1.0");
        await PublishVersionAsync("2.0.0");

        await _catalog.UnpublishAllVersionsAsync(BlueprintName);

        Assert.False(_git.Has(BlueprintFilePath("1.0.0")));
        Assert.False(_git.Has(BlueprintFilePath("1.1.0")));
        Assert.False(_git.Has(BlueprintFilePath("2.0.0")));
        Assert.False(_git.Has(VersionsCatalogPath(1)));
        Assert.False(_git.Has(VersionsCatalogPath(2)));
        Assert.False(_git.Has(LibraryCatalogPath()));
        Assert.DoesNotContain(BlueprintName, _git.Content(RootCatalogPath())!);
    }

    [Fact]
    public async Task UnpublishAsync_MissingVersion_IsIdempotentNoOp()
    {
        await PublishVersionAsync("1.0.0");
        var before = _git.FileCount;

        // A version that was never published must not throw and must not change anything.
        await _catalog.UnpublishAsync(new BlueprintId(BlueprintName, "9.9.9"));

        Assert.Equal(before, _git.FileCount);
        Assert.True(_git.Has(BlueprintFilePath("1.0.0")));
    }

    private async Task PublishVersionAsync(string version)
    {
        var dto = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId(BlueprintName, version),
            Description = $"Test blueprint {version}"
        };

        var sourceDir = Path.Combine(_tempDir, $"src-{version}");
        Directory.CreateDirectory(Path.Combine(sourceDir, "seed-data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "blueprint.yaml"),
            $"id: {BlueprintName}-{version}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "seed-data", "entities.yaml"), "entities: []");

        await _catalog.PublishAsync(dto, sourceDir, force: true);
    }

    // Path builders mirror GitHubBlueprintCatalog's private CreateBlueprintDirectoryPath / catalog paths.
    private static string BlueprintDir(string version)
        => $"blueprints/v1/m/{BlueprintName}/{new BlueprintId(BlueprintName, version).Version.Major}/{BlueprintName}-{version}";

    private static string BlueprintFilePath(string version) => $"{BlueprintDir(version)}/blueprint.yaml";
    private static string SeedFilePath(string version) => $"{BlueprintDir(version)}/seed-data/entities.yaml";
    private static string VersionsCatalogPath(int major) => $"blueprints/v1/m/{BlueprintName}/{major}/catalog.json";
    private static string LibraryCatalogPath() => $"blueprints/v1/m/{BlueprintName}/catalog.json";
    private static string RootCatalogPath() => "blueprints/v1/catalog.json";
}

/// <summary>
/// In-memory stand-in for <see cref="IGitHubClientWrapper" /> backed by a path → (content, sha) dictionary.
/// Sha is ignored on write (deletion/update are unconditional) which is sufficient for round-trip tests;
/// the conflict-retry behaviour is the real wrapper's concern, not the catalog's.
/// </summary>
internal sealed class InMemoryGitHubClientWrapper : IGitHubClientWrapper
{
    private readonly Dictionary<string, (string content, string sha)> _files = new(StringComparer.Ordinal);
    private int _shaCounter;

    public int FileCount => _files.Count;
    public bool Has(string path) => _files.ContainsKey(path);
    public string? Content(string path) => _files.TryGetValue(path, out var v) ? v.content : null;

    public Task<(string, string)?> GetFileAsync(string filePath)
        => Task.FromResult(_files.TryGetValue(filePath, out var v) ? ((string, string)?)(v.content, v.sha) : null);

    public Task UpdateFileAsync(string filePath, string commitMessage, string content, string sha)
    {
        _files[filePath] = (content, NextSha());
        return Task.CompletedTask;
    }

    public Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        _files[filePath] = (content, NextSha());
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string filePath, string commitMessage, string sha)
    {
        _files.Remove(filePath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string path, string sha)>> ListFilesRecursiveAsync(string directoryPath)
    {
        var prefix = directoryPath.TrimEnd('/') + "/";
        IReadOnlyList<(string, string)> result = _files
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => (kv.Key, kv.Value.sha))
            .ToList();
        return Task.FromResult(result);
    }

    private string NextSha() => $"sha{++_shaCounter}";
}
