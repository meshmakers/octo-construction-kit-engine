using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     AB#5433: unpublishing a model version from a catalog. The same scenarios run against the local file system
///     catalog and against the GitHub catalog (private and public) on a stateful in-memory GitHub client; no real
///     GitHub repository is ever written.
/// </summary>
public abstract class CatalogUnpublishTestsBase : IDisposable
{
    protected abstract ICatalog Catalog { get; }
    protected abstract string CatalogName { get; }

    /// <summary>All catalog files with their content (cache and lock files excluded).</summary>
    protected abstract Dictionary<string, string> Snapshot();

    protected abstract bool PublicPolicy { get; }
    protected abstract string? CommitLog { get; }

    private ICatalogService? _service;

    protected ICatalogService Service => _service ??= new CatalogService(NullLogger<CatalogService>.Instance,
        new CatalogManager(NullLogger<CatalogManager>.Instance, [Catalog]), A.Fake<ICkSerializer>(),
        A.Fake<ICatalogModelResolver>(), A.Fake<ICkCacheService>(),
        new Lazy<Meshmakers.Octo.ConstructionKit.Engine.SemVer.CkPublishGate>(() => throw new InvalidOperationException("publish not used")));

    public abstract void Dispose();

    protected static CkCompiledModelRoot Model(string name, string version, string? description = null,
        int? ckLanguage = null, CkModelId? exactDependency = null, (string Name, string Range, string Floor)? range = null)
    {
        var model = new CkCompiledModelRoot
        {
            ModelId = new CkModelId(name, version),
            Description = description ?? $"{name} {version}",
            CkLanguage = ckLanguage,
            MinEngineVersion = ckLanguage == 2 ? "3.4.0" : null
        };
        if (exactDependency != null)
        {
            model.Dependencies = [exactDependency];
        }

        if (range != null)
        {
            model.DependencyRanges =
            [
                new CkModelDependencyDto
                {
                    Range = new CkModelIdVersionRange(range.Value.Name, range.Value.Range), Floor = range.Value.Floor
                }
            ];
            model.MinEngineVersion = "3.4.0";
        }

        return model;
    }

    protected async Task<CkVersion?> LatestAsync(string name)
    {
        await Catalog.RefreshCatalogAsync(forceRefresh: true);
        var versions = new List<CkVersion>();
        await foreach (var item in Catalog.ListAsync(null))
        {
            if (item.ModelId.Name == name)
            {
                versions.Add(item.ModelId.Version);
            }
        }

        return versions.Count == 0 ? null : (CkVersion?)versions.Max();
    }

    protected async Task<List<string>> ListedAsync()
    {
        await Catalog.RefreshCatalogAsync(forceRefresh: true);
        var ids = new List<string>();
        await foreach (var item in Catalog.ListAsync(null))
        {
            ids.Add(item.ModelId.FullName);
        }

        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    private string Reason => "premature publish";

    [Fact]
    public async Task DryRun_ChangesNothing_AndNamesNoDependents()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0"));
        var before = Snapshot();

        var plan = await Service.PlanUnpublishAsync(CatalogName, new CkModelId("Provider", "1.1.0"),
            TestContext.Current.CancellationToken);

        Assert.True(plan.IsPublished);
        Assert.Empty(plan.Dependents);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Apply_RemovesExactlyTheTargetedVersion_FromIndexAndFiles_InBothRoots()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0", ckLanguage: 2));
        await Catalog.PublishAsync(Model("Provider", "1.2.0"));
        var other = Model("Other", "1.0.0");
        await Catalog.PublishAsync(other);
        Assert.Equal(["Other-1.0.0", "Provider-1.0.0", "Provider-1.1.0", "Provider-1.2.0"], await ListedAsync());

        // a v3 version (ckLanguage 2), then a v2 version
        Assert.Equal(CatalogUnpublishResult.Removed,
            await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.1.0"), Reason,
                TestContext.Current.CancellationToken));
        Assert.Equal(["Other-1.0.0", "Provider-1.0.0", "Provider-1.2.0"], await ListedAsync());
        Assert.False(await Catalog.IsExistingAsync(new CkModelId("Provider", "1.1.0")));
        Assert.Equal(new CkVersion("1.2.0"), await LatestAsync("Provider"));

        Assert.Equal(CatalogUnpublishResult.Removed,
            await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.2.0"), Reason,
                TestContext.Current.CancellationToken));
        Assert.Equal(["Other-1.0.0", "Provider-1.0.0"], await ListedAsync());
        Assert.Equal(new CkVersion("1.0.0"), await LatestAsync("Provider"));
        Assert.True(await Catalog.IsExistingAsync(new CkModelId("Other", "1.0.0")));
    }

    [Fact]
    public async Task LastVersionOfAModel_RemovesTheModelEntry_ButKeepsTheBurnRecord()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Other", "1.0.0"));

        await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.0.0"), Reason,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Other-1.0.0"], await ListedAsync());
        var files = Snapshot();
        Assert.DoesNotContain(files.Keys, k => k.Contains("ck-provider-"));
        Assert.DoesNotContain(files.Where(f => f.Key.EndsWith("ck-models/v2/catalog.json")),
            f => f.Value.Contains("Provider"));
        Assert.Contains(files, f => f.Key.EndsWith("Provider/withdrawn.json") && f.Value.Contains("1.0.0"));
    }

    [Fact]
    public async Task UnknownVersion_FailsClearly_AndChangesNothing()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        var before = Snapshot();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "9.9.9"), Reason,
                TestContext.Current.CancellationToken));

        Assert.Contains("Provider-9.9.9", exception.Message);
        Assert.Contains("not found", exception.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task RerunningUnpublish_IsIdempotent()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0"));
        var id = new CkModelId("Provider", "1.1.0");
        await Service.UnpublishAsync(CatalogName, id, Reason, TestContext.Current.CancellationToken);
        var after = Snapshot();

        var second = await Service.UnpublishAsync(CatalogName, id, Reason, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogUnpublishResult.AlreadyWithdrawn, second);
        Assert.Equal(after, Snapshot());
        var plan = await Service.PlanUnpublishAsync(CatalogName, id, TestContext.Current.CancellationToken);
        Assert.False(plan.IsPublished);
    }

    [Fact]
    public async Task ExactPinDependent_RefusesTheUnpublish_AlsoOnApply_AndNamesIt()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0"));
        await Catalog.PublishAsync(Model("Consumer", "1.0.0", exactDependency: new CkModelId("Provider", "1.1.0")));
        var before = Snapshot();

        var plan = await Service.PlanUnpublishAsync(CatalogName, new CkModelId("Provider", "1.1.0"),
            TestContext.Current.CancellationToken);
        var dependent = Assert.Single(plan.Dependents);
        Assert.Equal("Consumer-1.0.0", dependent.ModelId.FullName);

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.1.0"), Reason,
                TestContext.Current.CancellationToken));
        Assert.Contains("Consumer-1.0.0", exception.Message);
        Assert.Equal(before, Snapshot());

        // The pin is on 1.1.0 only: 1.0.0 can go.
        await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.0.0"), Reason,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RangeDependent_RefusesOnlyWhenTheVersionIsTheOnlySatisfyingOne()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0"));
        // floor 1.1.0: only 1.1.0 satisfies the effective range
        await Catalog.PublishAsync(Model("Consumer", "1.0.0", range: ("Provider", "[1.0,2.0)", "1.1.0")));

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.1.0"), Reason,
                TestContext.Current.CancellationToken));
        Assert.Contains("Consumer-1.0.0", exception.Message);

        // 1.0.0 is not satisfying the effective range [1.1,2.0): not a dependency target.
        await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.0.0"), Reason,
            TestContext.Current.CancellationToken);

        // Another satisfying version exists: the dependent is not left dangling.
        await Catalog.PublishAsync(Model("Provider", "1.2.0"));
        Assert.Equal(CatalogUnpublishResult.Removed, await Service.UnpublishAsync(CatalogName,
            new CkModelId("Provider", "1.1.0"), Reason, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BurnedNumber_IsNeverPublishedAgain_ButTheNextNumberWorks()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0", "original"));
        await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.1.0"), Reason,
            TestContext.Current.CancellationToken);

        var different = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Catalog.PublishAsync(Model("Provider", "1.1.0", "different content")));
        Assert.Contains("unpublished", different.Message);
        Assert.Contains("burned", different.Message);
        Assert.Contains(Reason, different.Message);

        // not even forced, and not even with identical content
        await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Catalog.PublishAsync(Model("Provider", "1.1.0", "original"), force: true));
        Assert.False(await Catalog.IsExistingAsync(new CkModelId("Provider", "1.1.0")));

        await Catalog.PublishAsync(Model("Provider", "1.1.1"));
        Assert.Equal(new CkVersion("1.1.1"), await LatestAsync("Provider"));
    }

    [Fact]
    public async Task BurnedNumber_SurvivesWhenTheWholeModelWasRemoved()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.0.0"), Reason,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ModelCatalogException>(() => Catalog.PublishAsync(Model("Provider", "1.0.0")));
        await Catalog.PublishAsync(Model("Provider", "1.0.1"));
        Assert.Equal(["Provider-1.0.1"], await ListedAsync());
    }

    [Fact]
    public async Task PublicCatalogPolicy_NeedsAReason_AndWritesItToTheCommitMessage()
    {
        await Catalog.PublishAsync(Model("Provider", "1.0.0"));
        await Catalog.PublishAsync(Model("Provider", "1.1.0"));
        var id = new CkModelId("Provider", "1.1.0");

        if (PublicPolicy)
        {
            var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
                Service.UnpublishAsync(CatalogName, id, null, TestContext.Current.CancellationToken));
            Assert.Contains("requires a reason", exception.Message);
            Assert.True(await Catalog.IsExistingAsync(id));
        }
        else
        {
            // private and dev catalogs need no reason
            var withoutReason = Model("Provider", "1.2.0");
            await Catalog.PublishAsync(withoutReason);
            await Service.UnpublishAsync(CatalogName, new CkModelId("Provider", "1.2.0"), null,
                TestContext.Current.CancellationToken);
        }

        await Service.UnpublishAsync(CatalogName, id, "System-2.3.0 broke the platform build",
            TestContext.Current.CancellationToken);

        if (CommitLog != null)
        {
            Assert.Contains("System-2.3.0 broke the platform build", CommitLog);
        }

        Assert.Contains(Snapshot(), f => f.Key.EndsWith("Provider/withdrawn.json") &&
                                         f.Value.Contains("System-2.3.0 broke the platform build"));
    }
}

public sealed class LocalFileSystemCatalogUnpublishTests : CatalogUnpublishTestsBase
{
    private readonly string _root;
    private readonly LocalFileSystemCatalog _catalog;

    public LocalFileSystemCatalogUnpublishTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LocalFileSystemCatalogUnpublishTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _catalog = new LocalFileSystemCatalog(Options.Create(new LocalFileSystemCatalogOptions
        {
            CacheDirectory = Path.Combine(_root, "cache"), RootPath = _root
        }), new CkJsonSerializer());
    }

    protected override ICatalog Catalog => _catalog;
    protected override string CatalogName => LocalFileSystemCatalog.Name;
    protected override bool PublicPolicy => false;
    protected override string? CommitLog => null;

    protected override Dictionary<string, string> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}") &&
                        !f.EndsWith(".lock"))
            .ToDictionary(f => Path.GetRelativePath(_root, f).Replace('\\', '/'), File.ReadAllText);

    public override void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}

public abstract class GitHubCatalogUnpublishTestsBase : CatalogUnpublishTestsBase
{
    private readonly InMemoryGitHubClientWrapper _git = new();
    private readonly string _tempDir;
    private readonly GitHubCatalog _catalog;

    protected GitHubCatalogUnpublishTestsBase(bool isPublic)
    {
        PublicPolicy = isPublic;
        _tempDir = Path.Combine(Path.GetTempPath(), "GitHubCatalogUnpublishTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();
        A.CallTo(() => gitHubClientFactory.CreateClient(A<GitHubCatalogOptions>._)).Returns(_git);
        var httpClientFactory = A.Fake<IHttpClientFactory>();

        // No GitHubPagesUri: reads go through the (in-memory) GitHub client, so the whole round trip is local.
        if (isPublic)
        {
            _catalog = new PublicGitHubCatalog(new CkJsonSerializer(), httpClientFactory, gitHubClientFactory,
                Options.Create(new PublicGitHubCatalogOptions
                {
                    CacheDirectory = _tempDir, GitHubApiToken = "test-token", GitHubRepositoryOwner = "o",
                    GitHubRepositoryName = "r", GitHubRepositoryBranch = "main", GitHubPagesUri = "",
                    RefreshNotFoundRetryCount = 0, RefreshNotFoundRetryDelaySeconds = 0
                }));
        }
        else
        {
            _catalog = new PrivateGitHubCatalog(new CkJsonSerializer(), httpClientFactory, gitHubClientFactory,
                Options.Create(new PrivateGitHubCatalogOptions
                {
                    CacheDirectory = _tempDir, GitHubApiToken = "test-token", GitHubRepositoryOwner = "o",
                    GitHubRepositoryName = "r", GitHubRepositoryBranch = "main", GitHubPagesUri = "",
                    RefreshNotFoundRetryCount = 0, RefreshNotFoundRetryDelaySeconds = 0
                }));
        }
    }

    protected override ICatalog Catalog => _catalog;
    protected override string CatalogName => _catalog.CatalogName;
    protected override bool PublicPolicy { get; }
    protected override string? CommitLog => string.Join("\n", _git.CommitMessages);
    protected override Dictionary<string, string> Snapshot() => _git.Snapshot();

    public override void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}

public sealed class PrivateGitHubCatalogUnpublishTests() : GitHubCatalogUnpublishTestsBase(false);

public sealed class PublicGitHubCatalogUnpublishTests() : GitHubCatalogUnpublishTestsBase(true);
