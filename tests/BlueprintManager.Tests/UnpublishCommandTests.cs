using FakeItEasy;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Behavioural tests for <see cref="UnpublishCommand" />. Arguments are driven through the real argument
/// parser (<c>ParseLayer</c>) and the catalog manager is faked, so these assert the command's force gating,
/// single-vs-all-versions routing, and idempotent no-op without touching any catalog.
/// </summary>
public class UnpublishCommandTests
{
    private const string Catalog = LocalFileSystemBlueprintCatalog.Name;

    [Fact]
    public async Task Execute_WithoutForce_DoesNotCallManager()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"));
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(A<string>._, A<BlueprintId>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
        A.CallTo(() => manager.UnpublishAllVersionsAsync(A<string>._, A<string>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Execute_WithForce_SingleVersion_CallsUnpublishAsync()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"));
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0", "-f"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(Catalog,
                A<BlueprintId>.That.Matches(b => b.FullName == "MyBlueprint-1.0.0"),
                A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_WithForce_NoVersion_CallsUnpublishAllVersions()
    {
        var manager = ManagerWith(("MyBlueprint", "1.0.0"), ("MyBlueprint", "1.1.0"));
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-f"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAllVersionsAsync(Catalog, "MyBlueprint", A<object?>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Execute_MissingBlueprint_DoesNotCallManager()
    {
        var manager = ManagerWith(); // empty catalog
        var cmd = CreateCommand(manager);
        cmd.CommandArgumentValue.ParseLayer(["-b", "MyBlueprint", "-r", "1.0.0", "-f"]);

        await cmd.Execute();

        A.CallTo(() => manager.UnpublishAsync(A<string>._, A<BlueprintId>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    private static IBlueprintCatalogManager ManagerWith(params (string name, string version)[] items)
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        var result = new BlueprintListResult
        {
            Items = items.Select(i => new BlueprintCatalogResultItem
            {
                CatalogName = Catalog,
                BlueprintId = new BlueprintId(i.name, i.version),
                Description = "d"
            }).ToList()
        };
        A.CallTo(() => manager.ListAsync(A<int>._, A<int>._, A<object?>._, A<CancellationToken?>._)).Returns(result);
        return manager;
    }

    private static UnpublishCommand CreateCommand(IBlueprintCatalogManager manager)
        => new(NullLogger<UnpublishCommand>.Instance, Options.Create(new BpmToolOptions()), manager);
}
