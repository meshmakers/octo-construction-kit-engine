using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#5650: GetUpdateInfoAsync read the catalog through a fixed ListAsync(0, 1000) window, so a
/// newer version beyond that window was never offered as an update. The versions of the installed
/// blueprint must come from the unpaged per-name lookup.
/// </summary>
public class BlueprintServiceUpdateInfoTests
{
    private const string TenantId = "tenant-a";
    private const string BlueprintName = "Samples.Photovoltaics";

    private readonly IBlueprintCatalogManager _catalogManager = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(),
        _catalogManager,
        _history,
        A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(),
        A.Fake<ICkModelUpgradeService>(),
        A.Fake<IRuntimeRepositoryProvider>(),
        A.Fake<IImportRtModelCommand>(),
        A.Fake<IRtYamlSerializer>(),
        A.Fake<IBlueprintNotifications>(),
        A.Fake<IBlueprintDependencyResolver>(),
        A.Fake<ITenantBlueprintInstallations>(),
        A.Fake<IBlueprintVariableProvider>(),
        NullLogger<BlueprintService>.Instance);

    private static BlueprintCatalogResultItem Item(string fullName, string catalogName = "Private") =>
        new() { BlueprintId = new BlueprintId(fullName), CatalogName = catalogName };

    private void GivenInstalled(string fullName) =>
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(TenantId, BlueprintName, A<CancellationToken>._))
            .Returns(new TenantBlueprintInfo { BlueprintId = new BlueprintId(fullName), AppliedAt = DateTime.UtcNow });

    [Fact]
    public async Task GetUpdateInfoAsync_FindsVersionsBeyondTheOldThousandEntryWindow()
    {
        GivenInstalled($"{BlueprintName}-1.0.0");
        // The first 1000 merged entries contain no version of the blueprint at all ...
        A.CallTo(() => _catalogManager.ListAsync(A<int>._, A<int>._, A<object?>._, A<CancellationToken?>._))
            .Returns(new BlueprintListResult
            {
                Items = Enumerable.Range(0, 1000).Select(i => Item($"Aaa{i:D4}-1.0.0", "Public")).ToList(),
                TotalCount = 1002
            });
        // ... the per-name lookup sees all of them.
        A.CallTo(() => _catalogManager.ListVersionsAsync(BlueprintName, A<object?>._, A<CancellationToken?>._))
            .Returns(new List<BlueprintCatalogResultItem>
            {
                Item($"{BlueprintName}-1.0.0", "Public"),
                Item($"{BlueprintName}-1.1.1"),
                Item($"{BlueprintName}-1.10.0"),
                Item($"{BlueprintName}-1.2.0")
            });
        A.CallTo(() => _catalogManager.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._,
                A<CancellationToken?>._))
            .Returns(new BlueprintMetaRootDto());

        var info = await CreateService().GetUpdateInfoAsync(TenantId, BlueprintName, TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Equal(
            [$"{BlueprintName}-1.1.1", $"{BlueprintName}-1.2.0", $"{BlueprintName}-1.10.0"],
            info.AvailableVersions.Select(v => v.FullName).ToArray());
        Assert.Equal($"{BlueprintName}-1.10.0", info.RecommendedVersion?.FullName);
        A.CallTo(() => _catalogManager.ListAsync(A<int>._, A<int>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task GetUpdateInfoAsync_NoNewerVersion_ReturnsNoUpdates()
    {
        GivenInstalled($"{BlueprintName}-1.1.1");
        A.CallTo(() => _catalogManager.ListVersionsAsync(BlueprintName, A<object?>._, A<CancellationToken?>._))
            .Returns(new List<BlueprintCatalogResultItem> { Item($"{BlueprintName}-1.0.0"), Item($"{BlueprintName}-1.1.1") });

        var info = await CreateService().GetUpdateInfoAsync(TenantId, BlueprintName, TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Empty(info.AvailableVersions);
        Assert.Null(info.RecommendedVersion);
    }
}
