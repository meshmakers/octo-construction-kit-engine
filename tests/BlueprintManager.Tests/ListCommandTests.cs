using FakeItEasy;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Behavioural tests for <see cref="ListCommand" /> (AB#5650): the command must read the complete merged
/// listing by paging until the total count instead of a fixed <c>ListAsync(0, 10000)</c> window, and must
/// print each catalog header once even though the manager returns the entries sorted by name/version.
/// </summary>
public class ListCommandTests
{
    [Fact]
    public async Task Execute_MoreEntriesThanFormerLimit_ListsAll()
    {
        const int total = 10_050; // beyond the former take: 10000 window
        var all = Enumerable.Range(0, total)
            .Select(i => Item("CatalogA", $"Bp{i:D5}", "1.0.0"))
            .ToList();
        var manager = PagingManager(all, ("CatalogA", "a"));
        var logger = new CapturingLogger<ListCommand>();
        var cmd = new ListCommand(logger, Options.Create(new BpmToolOptions()), manager);

        await cmd.Execute();

        Assert.Contains(logger.Messages, m => m == $"Found {total} blueprint(s)");
        Assert.Contains(logger.Messages, m => m.Contains($"Bp{total - 1:D5}-1.0.0"));
    }

    [Fact]
    public async Task Execute_NeverRequestsMoreThanOnePage()
    {
        var all = Enumerable.Range(0, ListCommand.PageSize * 2 + 1)
            .Select(i => Item("CatalogA", $"Bp{i:D5}", "1.0.0"))
            .ToList();
        var manager = PagingManager(all, ("CatalogA", "a"));
        var cmd = new ListCommand(new CapturingLogger<ListCommand>(), Options.Create(new BpmToolOptions()), manager);

        await cmd.Execute();

        A.CallTo(() => manager.ListAsync(A<int>._, A<int>.That.Not.IsEqualTo(ListCommand.PageSize),
                A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
        A.CallTo(() => manager.ListAsync(A<int>._, ListCommand.PageSize, A<object?>._, A<CancellationToken?>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task Execute_EmptyPageBeforeTotalCount_Terminates()
    {
        // A TotalCount larger than what the manager actually serves (e.g. catalog changed between calls)
        // must not make the command loop forever.
        var manager = A.Fake<IBlueprintCatalogManager>();
        A.CallTo(() => manager.GetCatalogList(A<object?>._)).Returns([]);
        A.CallTo(() => manager.ListAsync(0, A<int>._, A<object?>._, A<CancellationToken?>._))
            .Returns(new BlueprintListResult { Items = [Item("CatalogA", "Bp", "1.0.0")], TotalCount = 5 });
        A.CallTo(() => manager.ListAsync(A<int>.That.Not.IsEqualTo(0), A<int>._, A<object?>._, A<CancellationToken?>._))
            .Returns(new BlueprintListResult { Items = [], TotalCount = 5 });
        var logger = new CapturingLogger<ListCommand>();
        var cmd = new ListCommand(logger, Options.Create(new BpmToolOptions()), manager);

        await cmd.Execute();

        Assert.Contains(logger.Messages, m => m == "Found 1 blueprint(s)");
    }

    [Fact]
    public async Task Execute_GroupsByCatalogInCatalogOrder()
    {
        // The manager returns entries sorted by name/version across catalogs; the output must still show
        // one header per catalog, in catalog order.
        var all = new List<BlueprintCatalogResultItem>
        {
            Item("Private", "Alpha", "1.1.0"),
            Item("Public", "Alpha", "1.0.0"),
            Item("Private", "Beta", "2.0.0"),
            Item("Public", "Gamma", "1.0.0")
        };
        var manager = PagingManager(all, ("Public", "pub"), ("Private", "priv"));
        var logger = new CapturingLogger<ListCommand>();
        var cmd = new ListCommand(logger, Options.Create(new BpmToolOptions()), manager);

        await cmd.Execute();

        var headers = logger.Messages.Where(m => m.StartsWith("Catalog: ")).ToList();
        Assert.Equal(["Catalog: Public", "Catalog: Private"], headers);
        var entries = logger.Messages.Where(m => m.StartsWith("  ")).ToList();
        Assert.Equal(
        [
            "  Alpha-1.0.0 - d", "  Gamma-1.0.0 - d",
            "  Alpha-1.1.0 - d", "  Beta-2.0.0 - d"
        ], entries);
    }

    private static IBlueprintCatalogManager PagingManager(List<BlueprintCatalogResultItem> all,
        params (string name, string description)[] catalogs)
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        A.CallTo(() => manager.GetCatalogList(A<object?>._))
            .Returns(catalogs.Select(c => new Tuple<string, string>(c.name, c.description)).ToList());
        A.CallTo(() => manager.ListAsync(A<int>._, A<int>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((int skip, int take, object? _, CancellationToken? _) => new BlueprintListResult
            {
                Items = all.Skip(skip).Take(take).ToList(),
                TotalCount = all.Count
            });
        return manager;
    }

    private static BlueprintCatalogResultItem Item(string catalog, string name, string version) => new()
    {
        CatalogName = catalog,
        BlueprintId = new BlueprintId(name, version),
        Description = "d"
    };
}
