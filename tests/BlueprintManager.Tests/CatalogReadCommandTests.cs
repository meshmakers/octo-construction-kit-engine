using FakeItEasy;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Verifies that catalog-read commands force a catalog refresh before they run, so they never serve a
/// stale on-disk cache. The refresh happens in <c>PreValidate</c>, the hook the command parser invokes
/// before <c>Execute</c>.
/// </summary>
public class CatalogReadCommandTests
{
    [Fact]
    public async Task ListCommand_PreValidate_ForcesCatalogRefresh()
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        var cmd = new ListCommand(NullLogger<ListCommand>.Instance, Options.Create(new BpmToolOptions()), manager);

        await cmd.PreValidate();

        A.CallTo(() => manager.RefreshAllCatalogCachesAsync(A<object?>._, true)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetCommand_PreValidate_ForcesCatalogRefresh()
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        var cmd = new GetCommand(NullLogger<GetCommand>.Instance, Options.Create(new BpmToolOptions()), manager);

        await cmd.PreValidate();

        A.CallTo(() => manager.RefreshAllCatalogCachesAsync(A<object?>._, true)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UnpublishCommand_PreValidate_ForcesCatalogRefresh()
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        var cmd = new UnpublishCommand(NullLogger<UnpublishCommand>.Instance, Options.Create(new BpmToolOptions()), manager);

        await cmd.PreValidate();

        A.CallTo(() => manager.RefreshAllCatalogCachesAsync(A<object?>._, true)).MustHaveHappenedOnceExactly();
    }
}
