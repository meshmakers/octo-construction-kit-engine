using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
/// AB#5650: the CK model migration service looked up the latest version of a model in a fixed
/// <c>ListAsync(0, 1000)</c> window, so versions beyond the first 1000 catalog entries were invisible.
/// <see cref="CatalogManager.ListVersionsAsync" /> returns all versions of one model unpaged; the merged
/// listing must apply skip/take exactly once on the deduplicated list (lowest catalog order wins).
/// </summary>
public class CatalogManagerListingTests
{
    private const string PublicCatalog = "Public";
    private const string PrivateCatalog = "Private";

    private static ICatalog FakeCatalog(string name, int order, IEnumerable<string> modelIds)
    {
        var catalog = A.Fake<ICatalog>();
        A.CallTo(() => catalog.CatalogName).Returns(name);
        A.CallTo(() => catalog.Order).Returns(order);
        A.CallTo(() => catalog.CanRead).Returns(true);
        A.CallTo(() => catalog.IsSupportingSourceIdentifier(A<object?>._)).Returns(true);
        var items = modelIds.Select(id => new CatalogResultItem
        {
            ModelId = new CkModelId(id),
            CatalogName = name,
            Description = $"{id} from {name}"
        }).ToList();
        A.CallTo(() => catalog.ListAsync(A<object?>._)).ReturnsLazily(() => items.ToAsyncEnumerable());
        A.CallTo(() => catalog.SearchAsync(A<string>._, A<object?>._))
            .ReturnsLazily((string term, object? _) => items
                .Where(i => i.ModelId.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToAsyncEnumerable());
        return catalog;
    }

    private static CatalogManager Manager(params ICatalog[] catalogs)
        => new(NullLogger<CatalogManager>.Instance, catalogs);

    /// <summary>Public (order 10) and private (order 20) overlap on Energy-1.0.1.</summary>
    private static CatalogManager OverlappingCatalogs() => Manager(
        // Registered out of order on purpose: catalog order, not registration order, decides.
        FakeCatalog(PrivateCatalog, 20, ["Energy-1.10.0", "Energy-1.1.1", "Energy-1.0.1", "Alpha-2.0.0"]),
        FakeCatalog(PublicCatalog, 10, ["Zeta-1.0.0", "Energy-1.0.1", "Energy-1.0.0", "Alpha-1.0.0"]));

    [Fact]
    public async Task ListVersionsAsync_MergesCatalogs_DedupesByLowestOrder_SortsBySemVer()
    {
        var versions = await OverlappingCatalogs().ListVersionsAsync("Energy",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Energy-1.0.0", "Energy-1.0.1", "Energy-1.1.1", "Energy-1.10.0"],
            versions.Select(v => v.ModelId.ToString()));
        Assert.Equal(PublicCatalog, versions.Single(v => v.ModelId.ToString() == "Energy-1.0.1").CatalogName);
        Assert.Equal(PrivateCatalog, versions.Single(v => v.ModelId.ToString() == "Energy-1.10.0").CatalogName);
    }

    [Fact]
    public async Task ListVersionsAsync_MatchesNameCaseInsensitivelyAndExactly()
    {
        var manager = Manager(FakeCatalog(PublicCatalog, 10, ["Energy-1.0.0", "EnergyCommunity-1.0.0"]));

        var versions = await manager.ListVersionsAsync("energy",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Energy-1.0.0"], versions.Select(v => v.ModelId.ToString()));
    }

    [Fact]
    public async Task ListVersionsAsync_IgnoresUnreadableCatalogs()
    {
        var unreadable = FakeCatalog(PrivateCatalog, 5, ["Energy-9.0.0"]);
        A.CallTo(() => unreadable.CanRead).Returns(false);
        var manager = Manager(unreadable, FakeCatalog(PublicCatalog, 10, ["Energy-1.0.0"]));

        var versions = await manager.ListVersionsAsync("Energy",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Energy-1.0.0"], versions.Select(v => v.ModelId.ToString()));
    }

    [Fact]
    public async Task ListVersionsAsync_EmptyName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => OverlappingCatalogs().ListVersionsAsync(" "));
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsVersionsBeyondTheOldFixedWindow()
    {
        // 1500 filler entries in the public catalog push the private catalog's newest version past
        // the old ListAsync(0, 1000) window.
        var filler = Enumerable.Range(0, 1500).Select(i => $"Filler{i:D4}-1.0.0");
        var manager = Manager(
            FakeCatalog(PublicCatalog, 10, filler.Append("Energy-1.0.0")),
            FakeCatalog(PrivateCatalog, 20, ["Energy-2.5.0"]));
        var ct = TestContext.Current.CancellationToken;

        var oldWindow = await manager.ListAsync(0, 1000, cancellationToken: ct);
        Assert.DoesNotContain(oldWindow.ModelResultItems, m => m.ModelId.Name == "Energy");

        var versions = await manager.ListVersionsAsync("Energy", cancellationToken: ct);

        Assert.Equal(["Energy-1.0.0", "Energy-2.5.0"], versions.Select(v => v.ModelId.ToString()));
    }

    [Fact]
    public async Task ListAsync_PagesAreContiguous_TotalCountIsDeduplicatedTotal()
    {
        var manager = OverlappingCatalogs();
        var ct = TestContext.Current.CancellationToken;

        var all = await manager.ListAsync(0, 100, cancellationToken: ct);
        var page1 = await manager.ListAsync(0, 3, cancellationToken: ct);
        var page2 = await manager.ListAsync(3, 3, cancellationToken: ct);
        var page3 = await manager.ListAsync(6, 3, cancellationToken: ct);

        // 8 raw entries, Energy-1.0.1 twice.
        Assert.Equal(7, all.TotalCount);
        Assert.Equal(7, page2.TotalCount);
        Assert.Equal(all.ModelResultItems.Select(m => m.ModelId),
            page1.ModelResultItems.Concat(page2.ModelResultItems).Concat(page3.ModelResultItems)
                .Select(m => m.ModelId));
        Assert.Equal(PublicCatalog,
            all.ModelResultItems.Single(m => m.ModelId.ToString() == "Energy-1.0.1").CatalogName);
    }

    [Fact]
    public async Task ListAsync_SingleCatalog_SkipIsAppliedOnce()
    {
        var manager = Manager(FakeCatalog(PublicCatalog, 10, ["A-1.0.0", "B-1.0.0", "C-1.0.0", "D-1.0.0"]));

        var page = await manager.ListAsync(PublicCatalog, 2, 2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["C-1.0.0", "D-1.0.0"], page.ModelResultItems.Select(m => m.ModelId.ToString()));
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_SingleCatalog_SkipIsAppliedOnce()
    {
        var manager = Manager(FakeCatalog(PublicCatalog, 10,
            ["Energy.A-1.0.0", "Energy.B-1.0.0", "Energy.C-1.0.0", "Other-1.0.0"]));

        var page = await manager.SearchAsync(PublicCatalog, "Energy", 1, 5,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Energy.B-1.0.0", "Energy.C-1.0.0"], page.ModelResultItems.Select(m => m.ModelId.ToString()));
        Assert.Equal(3, page.TotalCount);
    }
}
