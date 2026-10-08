using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// AB#5650: the merged listing used to append catalog by catalog (in catalog order) before
/// skip/take, so the newer versions only a lower-priority catalog serves (test-2: the private
/// catalog's Samples.Photovoltaics-1.1.x) landed behind the first page. The merged list must be
/// sorted by name, then semantic version, and the per-id dedupe (lowest order wins) must hold.
/// </summary>
public class BlueprintCatalogManagerListingTests
{
    private const string PublicCatalog = "Public";
    private const string PrivateCatalog = "Private";

    private static IBlueprintCatalog FakeCatalog(string name, int order, params string[] blueprintIds)
    {
        var catalog = A.Fake<IBlueprintCatalog>();
        A.CallTo(() => catalog.CatalogName).Returns(name);
        A.CallTo(() => catalog.Order).Returns(order);
        A.CallTo(() => catalog.CanRead).Returns(true);
        A.CallTo(() => catalog.IsSupportingSourceIdentifier(A<object?>._)).Returns(true);
        var items = blueprintIds.Select(id => new BlueprintCatalogResultItem
        {
            BlueprintId = new BlueprintId(id),
            CatalogName = name,
            Description = $"{id} from {name}"
        }).ToList();
        A.CallTo(() => catalog.ListAsync(A<object?>._)).ReturnsLazily(() => items.ToAsyncEnumerable());
        A.CallTo(() => catalog.SearchAsync(A<string>._, A<object?>._))
            .ReturnsLazily((string term, object? _) => items
                .Where(i => i.BlueprintId.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToAsyncEnumerable());
        return catalog;
    }

    private static BlueprintCatalogManager Manager(params IBlueprintCatalog[] catalogs)
        => new(NullLogger<BlueprintCatalogManager>.Instance, catalogs);

    /// <summary>Public (order 10) and private (order 20) overlap on Photovoltaics-1.0.1.</summary>
    private static BlueprintCatalogManager OverlappingCatalogs() => Manager(
        // Registered out of order on purpose: catalog order, not registration order, decides.
        FakeCatalog(PrivateCatalog, 20,
            "Samples.Photovoltaics-1.10.0", "Samples.Photovoltaics-1.1.1", "Samples.Photovoltaics-1.0.1",
            "Samples.Photovoltaics-1.2.0", "Alpha-2.0.0"),
        FakeCatalog(PublicCatalog, 10,
            "Zeta-1.0.0", "Samples.Photovoltaics-1.0.1", "Samples.Photovoltaics-1.0.0", "Alpha-1.0.0",
            "Beta-1.0.0"));

    private static readonly string[] ExpectedOrder =
    [
        "Alpha-1.0.0", "Alpha-2.0.0", "Beta-1.0.0",
        "Samples.Photovoltaics-1.0.0", "Samples.Photovoltaics-1.0.1", "Samples.Photovoltaics-1.1.1",
        "Samples.Photovoltaics-1.2.0", "Samples.Photovoltaics-1.10.0", "Zeta-1.0.0"
    ];

    [Fact]
    public async Task ListAsync_MergedList_IsSortedByNameThenSemanticVersion()
    {
        var result = await OverlappingCatalogs().ListAsync(0, 100);

        Assert.Equal(ExpectedOrder.Length, result.TotalCount);
        // 1.10.0 after 1.2.0 proves a semantic, not a lexicographic, version order.
        Assert.Equal(ExpectedOrder, result.Items.Select(i => i.BlueprintId.FullName).ToArray());
    }

    [Fact]
    public async Task ListAsync_DuplicateId_IsTakenFromTheLowestOrderCatalog()
    {
        var result = await OverlappingCatalogs().ListAsync(0, 100);

        var duplicate = Assert.Single(result.Items, i => i.BlueprintId.FullName == "Samples.Photovoltaics-1.0.1");
        Assert.Equal(PublicCatalog, duplicate.CatalogName);
        Assert.Equal(PrivateCatalog,
            Assert.Single(result.Items, i => i.BlueprintId.FullName == "Samples.Photovoltaics-1.1.1").CatalogName);
    }

    [Fact]
    public async Task ListAsync_PagesAcrossTheBoundary_AreContiguousAndComplete()
    {
        var manager = OverlappingCatalogs();

        var firstPage = await manager.ListAsync(0, 4);
        var secondPage = await manager.ListAsync(4, 4);
        var lastPage = await manager.ListAsync(8, 4);

        Assert.All([firstPage, secondPage, lastPage], p => Assert.Equal(ExpectedOrder.Length, p.TotalCount));
        Assert.Equal(4, firstPage.Items.Count);
        Assert.Equal(4, secondPage.Items.Count);
        Assert.Single(lastPage.Items);
        var concatenated = firstPage.Items.Concat(secondPage.Items).Concat(lastPage.Items)
            .Select(i => i.BlueprintId.FullName).ToArray();
        Assert.Equal(ExpectedOrder, concatenated);
        // The private catalog's newer version sits on the second page, next to its siblings,
        // instead of being appended after every public entry.
        Assert.Contains(secondPage.Items, i => i.BlueprintId.FullName == "Samples.Photovoltaics-1.1.1");
    }

    [Fact]
    public async Task SearchAsync_MergedResult_IsSortedAndDeduplicated()
    {
        var result = await OverlappingCatalogs().SearchAsync("Photovoltaics", 1, 3);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(["Samples.Photovoltaics-1.0.1", "Samples.Photovoltaics-1.1.1", "Samples.Photovoltaics-1.2.0"],
            result.Items.Select(i => i.BlueprintId.FullName).ToArray());
        Assert.Equal(PublicCatalog, result.Items[0].CatalogName);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsAllVersionsOfOneBlueprint_OrderedByVersion()
    {
        var versions = await OverlappingCatalogs().ListVersionsAsync("Samples.Photovoltaics");

        Assert.Equal(
            ["Samples.Photovoltaics-1.0.0", "Samples.Photovoltaics-1.0.1", "Samples.Photovoltaics-1.1.1",
             "Samples.Photovoltaics-1.2.0", "Samples.Photovoltaics-1.10.0"],
            versions.Select(i => i.BlueprintId.FullName).ToArray());
        Assert.Equal(PublicCatalog, versions[1].CatalogName);
    }

    [Fact]
    public async Task ListVersionsAsync_IsNotLimitedByAPageWindow()
    {
        // 1200 entries sort before the wanted blueprint, i.e. beyond the old ListAsync(0, 1000) window.
        var filler = Enumerable.Range(0, 1200).Select(i => $"Aaa{i:D4}-1.0.0").ToArray();
        var manager = Manager(
            FakeCatalog(PublicCatalog, 10, filler),
            FakeCatalog(PrivateCatalog, 20, "Zz.Wanted-1.0.0", "Zz.Wanted-2.0.0"));

        var window = await manager.ListAsync(0, 1000);
        Assert.DoesNotContain(window.Items, i => i.BlueprintId.Name == "Zz.Wanted");

        var versions = await manager.ListVersionsAsync("Zz.Wanted");

        Assert.Equal(["Zz.Wanted-1.0.0", "Zz.Wanted-2.0.0"], versions.Select(i => i.BlueprintId.FullName).ToArray());
    }

    [Fact]
    public async Task ListVersionsAsync_UnknownBlueprint_ReturnsEmpty()
    {
        var versions = await OverlappingCatalogs().ListVersionsAsync("DoesNotExist");

        Assert.Empty(versions);
    }
}
