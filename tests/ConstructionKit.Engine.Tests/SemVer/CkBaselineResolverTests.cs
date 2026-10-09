using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     AB#5450: the shared, read-only baseline resolver, rules 1–5, against a fake multi-catalog setup (local file-system
///     catalog + two published catalogs, several majors). Every test also asserts that the resolver wrote nothing.
/// </summary>
public class CkBaselineResolverTests
{
    private const string Model = "EnergyCommunity";
    private const string Private = "PrivateGitHubCatalog";
    private const string Public = "PublicGitHubCatalog";
    private const string Local = LocalFileSystemCatalog.Name;

    private readonly Dictionary<string, List<CkVersion>> _catalogs = new()
    {
        [Local] = [], [Private] = [], [Public] = []
    };

    private readonly HashSet<string> _unreachable = [];
    private readonly ICatalogService _catalogService = A.Fake<ICatalogService>();

    public CkBaselineResolverTests()
    {
        A.CallTo(() => _catalogService.GetCatalogList(A<object?>._))
            .ReturnsLazily(() => _catalogs.Keys.Select(name => Tuple.Create(name, name)).ToList());
        A.CallTo(() => _catalogService.IsExistingAsync(A<string>._, A<CkModelIdVersionRange>._, A<object?>._))
            .ReturnsLazily((string catalog, CkModelIdVersionRange range, object? _) =>
            {
                var highest = _catalogs[catalog]
                    .Where(v => range.ModelVersionRange.IsSatisfiedBy(v))
                    .OrderByDescending(v => v)
                    .Select(v => (CkVersion?)v)
                    .FirstOrDefault();
                return Task.FromResult(new ModelExistingResult
                {
                    Exists = highest != null,
                    ModelId = highest == null ? null : new CkModelId(Model, highest.Value),
                    CatalogName = catalog,
                    SourceUnreachable = _unreachable.Contains(catalog)
                });
            });
    }

    private void Published(string catalog, params string[] versions) =>
        _catalogs[catalog].AddRange(versions.Select(v => new CkVersion(v)));

    private async Task<CkBaselineResolution> ResolveAsync(string declared, string? catalogName = null)
    {
        var resolution = await new CkBaselineResolver(_catalogService).ResolveAsync(Model, new CkVersion(declared),
            catalogName);
        AssertNoCatalogWrite();
        return resolution;
    }

    /// <summary>
    ///     Rule 4 (read-only): no publish, no cache refresh, no restore on the catalog service.
    /// </summary>
    private static readonly string[] WriteMethods =
    [
        nameof(ICatalogService.PublishAsync), nameof(ICatalogService.RefreshCatalogCacheAsync),
        nameof(ICatalogService.RefreshAllCatalogCachesAsync), nameof(ICatalogService.RestoreConstructionKitModelsAsync)
    ];

    private void AssertNoCatalogWrite()
    {
        A.CallTo(_catalogService)
            .Where(call => WriteMethods.Contains(call.Method.Name))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Rule1_OlderMajorLine_BaselineIsNewestVersionOfTheSameMajor()
    {
        Published(Private, "3.1.0", "3.3.0", "4.0.0", "4.5.0");

        var resolution = await ResolveAsync("3.4.0");

        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
        Assert.Equal(Private, resolution.CatalogName);
        Assert.False(resolution.IsLocal);
        Assert.False(resolution.IsFromLowerMajor);
    }

    [Fact]
    public async Task Rule1_DeclaredBelowNewestOfItsMajor_BaselineIsTheNewestOfTheMajor()
    {
        Published(Private, "3.1.0", "3.3.0", "4.5.0");

        var resolution = await ResolveAsync("3.2.0");

        // ValidateVersion turns this into OCTO-CK101 against 3.3.0 (not against 4.5.0).
        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
    }

    [Fact]
    public async Task Rule1_NewestOfAllCatalogsWithinTheMajor()
    {
        Published(Private, "3.2.0");
        Published(Public, "3.3.0", "5.0.0");

        var resolution = await ResolveAsync("3.4.0");

        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
        Assert.Equal(Public, resolution.CatalogName);
    }

    [Fact]
    public async Task Rule2_NewMajor_BaselineIsNewestVersionOfThePreviousMajorLine()
    {
        Published(Private, "2.9.0", "3.1.0", "3.3.0");

        var resolution = await ResolveAsync("4.0.0");

        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
        Assert.True(resolution.IsFromLowerMajor);
        Assert.False(resolution.IsFirstPublication);
    }

    [Fact]
    public async Task Rule2_NewMajorAfterAGap_BaselineIsTheHighestLowerMajor()
    {
        Published(Private, "1.9.0", "2.5.0");

        var resolution = await ResolveAsync("4.0.0");

        Assert.Equal(new CkModelId(Model, "2.5.0"), resolution.Baseline);
        Assert.True(resolution.IsFromLowerMajor);
    }

    [Fact]
    public async Task Rule2_NoVersionAtAll_IsFirstPublication()
    {
        var resolution = await ResolveAsync("1.0.0");

        Assert.Null(resolution.Baseline);
        Assert.True(resolution.IsFirstPublication);
        Assert.False(resolution.IsFromLowerMajor);
    }

    [Fact]
    public async Task Rule2_UnreachableSource_NeverFallsBackToALowerMajor()
    {
        Published(Private, "2.5.0");
        _unreachable.Add(Public);

        var resolution = await ResolveAsync("3.1.0");

        // The same-major baseline may sit in the unreachable catalog: OCTO-CK102, not a lower-major baseline.
        Assert.Null(resolution.Baseline);
        Assert.True(resolution.SourceUnreachable);
        Assert.False(resolution.IsFirstPublication);
    }

    [Fact]
    public async Task Rule3_LocalEntriesAtOrAboveDeclared_AreIgnoredAndListed()
    {
        Published(Private, "3.3.0");
        Published(Local, "3.4.0", "3.5.0", "4.0.0");

        var resolution = await ResolveAsync("3.4.0");

        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
        Assert.False(resolution.IsLocal);
        Assert.Equal([new CkModelId(Model, "3.4.0"), new CkModelId(Model, "3.5.0")], resolution.IgnoredLocalEntries);
    }

    [Fact]
    public async Task Rule3_OnlyLocalSelfEntry_IsFirstPublicationNotSelfComparison()
    {
        Published(Local, "1.0.0");

        var resolution = await ResolveAsync("1.0.0");

        Assert.True(resolution.IsFirstPublication);
        Assert.Equal([new CkModelId(Model, "1.0.0")], resolution.IgnoredLocalEntries);
    }

    [Fact]
    public async Task Rule3_LocalEntryBelowDeclared_CanBeTheBaseline()
    {
        Published(Private, "3.3.0");
        Published(Local, "3.3.5");

        var resolution = await ResolveAsync("3.4.0");

        Assert.Equal(new CkModelId(Model, "3.3.5"), resolution.Baseline);
        Assert.True(resolution.IsLocal);
    }

    [Fact]
    public async Task Rule3_SameVersionLocalAndPublished_PrefersThePublishedEntry()
    {
        Published(Private, "3.3.0");
        Published(Local, "3.3.0");

        var resolution = await ResolveAsync("3.4.0");

        Assert.Equal(Private, resolution.CatalogName);
        Assert.False(resolution.IsLocal);
    }

    [Fact]
    public async Task Rule3_PublishedEntryEqualToDeclared_StaysTheBaseline()
    {
        Published(Private, "3.4.0");
        Published(Local, "3.4.0");

        var resolution = await ResolveAsync("3.4.0");

        // An unchanged version with structural changes must still require a bump (OCTO-CK100).
        Assert.Equal(new CkModelId(Model, "3.4.0"), resolution.Baseline);
        Assert.Equal(Private, resolution.CatalogName);
    }

    [Fact]
    public async Task Rule3_NewMajor_LocalEntriesOfTheDeclaredMajorAreIgnored()
    {
        Published(Private, "3.3.0");
        Published(Local, "4.0.0");

        var resolution = await ResolveAsync("4.0.0");

        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
        Assert.True(resolution.IsFromLowerMajor);
        Assert.Equal([new CkModelId(Model, "4.0.0")], resolution.IgnoredLocalEntries);
    }

    [Fact]
    public async Task Rule3_RepeatedResolution_GivesTheSameBaseline()
    {
        Published(Private, "1.0.0");

        var first = await ResolveAsync("1.1.0");
        // ValidateVersion registers the validated model in the local catalog (sibling resolution, AB#5434 symptom 1).
        Published(Local, "1.1.0");
        var second = await ResolveAsync("1.1.0");

        Assert.Equal(first.Baseline, second.Baseline);
        Assert.Equal(first.CatalogName, second.CatalogName);
        Assert.Equal([new CkModelId(Model, "1.1.0")], second.IgnoredLocalEntries);
    }

    [Fact]
    public async Task Rule4_PinnedCatalog_QueriesOnlyThatCatalog()
    {
        Published(Private, "3.3.0");
        Published(Public, "3.4.0");

        var resolution = await ResolveAsync("3.5.0", Private);

        Assert.Equal(new CkModelId(Model, "3.3.0"), resolution.Baseline);
        A.CallTo(() => _catalogService.IsExistingAsync(Public, A<CkModelIdVersionRange>._, A<object?>._))
            .MustNotHaveHappened();
        A.CallTo(() => _catalogService.GetCatalogList(A<object?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Rule5_ResultNamesBaselineCatalogLocalityAndCache()
    {
        Published(Local, "2.0.1");

        var resolution = await ResolveAsync("2.1.0");

        Assert.Equal(Model, resolution.ModelName);
        Assert.Equal(new CkVersion("2.1.0"), resolution.DeclaredVersion);
        Assert.Equal(Local, resolution.CatalogName);
        Assert.True(resolution.IsLocal);
        Assert.Empty(resolution.IgnoredLocalEntries);
    }
}
