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
///     AB#9578: the applied and updated notifications carry the CK model dependencies of the blueprint, so the services
///     can switch on the capability a blueprint needs (System.Communication, System.Ai) without reading catalogs.
///     Apply: the dependencies of every blueprint the resolution installs for the root (kept dependencies included).
///     Update: the dependencies of the target version. A failed operation raises no applied/updated notification.
/// </summary>
public class BlueprintServiceNotificationCkDependenciesTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Current = new("Starter-1.0.0");
    private static readonly BlueprintId Target = new("Starter-2.0.0");
    private static readonly BlueprintId Library = new("Library-1.0.0");

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _provider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly ICkModelUpgradeService _upgrade = A.Fake<ICkModelUpgradeService>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly IBlueprintNotifications _notifications = A.Fake<IBlueprintNotifications>();
    private readonly List<BlueprintAppliedNotification> _applied = [];
    private readonly List<BlueprintUpdatedNotification> _updated = [];

    public BlueprintServiceNotificationCkDependenciesTests()
    {
        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .Returns(new BlueprintMetaRootDto
            {
                BlueprintId = Target,
                CkModelDependencies =
                [
                    new CkModelIdVersionRange("System.Ai-[1.0,2.0)"),
                    new CkModelIdVersionRange("Basic-[2.5,3.0)")
                ]
            });
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(TenantId, "Starter", A<CancellationToken>._))
            .Returns(new TenantBlueprintInfo { BlueprintId = Current, AppliedAt = DateTime.UtcNow });
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
        A.CallTo(() => _upgrade.UpgradeModelsAsync(TenantId, A<IEnumerable<CkModelIdVersionRange>>._,
                A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .Returns(new CkModelUpgradeResult { Success = true });
        A.CallTo(() => _provider.GetSchemaVersionsAsync(TenantId, A<CancellationToken>._))
            .Returns(new Dictionary<string, string>());
        A.CallTo(() => _notifications.NotifyAppliedAsync(A<BlueprintAppliedNotification>._, A<CancellationToken>._))
            .Invokes((BlueprintAppliedNotification n, CancellationToken _) => _applied.Add(n))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _notifications.NotifyUpdatedAsync(A<BlueprintUpdatedNotification>._, A<CancellationToken>._))
            .Invokes((BlueprintUpdatedNotification n, CancellationToken _) => _updated.Add(n))
            .Returns(Task.CompletedTask);
    }

    private void GivenResolution(params BlueprintMetaRootDto[] installOrder) =>
        A.CallTo(() => _resolver.ResolveAsync(Target, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = Target,
                Success = true,
                InstallOrder = installOrder.ToList(),
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });

    private static BlueprintMetaRootDto Blueprint(BlueprintId id, params string[] ckRanges) => new()
    {
        BlueprintId = id,
        CkModelDependencies = ckRanges.Select(r => new CkModelIdVersionRange(r)).ToList()
    };

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(), _catalog, _history, A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(), _upgrade, _provider, A.Fake<IImportRtModelCommand>(),
        A.Fake<IRtYamlSerializer>(), _notifications, _resolver, _installations,
        A.Fake<IBlueprintVariableProvider>(), NullLogger<BlueprintService>.Instance);

    [Fact]
    public async Task Apply_CarriesTheCkDependenciesOfTheRootAndOfTheResolvedBlueprints_DistinctAndSorted()
    {
        GivenResolution(
            Blueprint(Library, "System.Communication-[4.0,5.0)", "Basic-[2.5,3.0)"),
            Blueprint(Target, "System.Ai-[1.0,2.0)", "Basic-[2.6,3.0)"));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target, false,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.OperationResult.Messages.Select(m => m.MessageText)));
        var applied = Assert.Single(_applied);
        Assert.Equal(TenantId, applied.TenantId);
        Assert.Equal(Target, applied.BlueprintId);
        Assert.Equal(["Basic", "System.Ai", "System.Communication"], applied.CkModelDependencies);
    }

    [Fact]
    public async Task Apply_BlueprintWithoutCkDependencies_CarriesAnEmptyListNotNull()
    {
        GivenResolution(Blueprint(Target));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target, false,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var applied = Assert.Single(_applied);
        Assert.NotNull(applied.CkModelDependencies);
        Assert.Empty(applied.CkModelDependencies);
    }

    [Fact]
    public async Task Apply_Failed_RaisesNoAppliedNotification()
    {
        GivenResolution(Blueprint(Target, "System.Ai-[1.0,2.0)"));
        A.CallTo(() => _upgrade.UpgradeModelsAsync(TenantId, A<IEnumerable<CkModelIdVersionRange>>._,
                A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .Returns(new CkModelUpgradeResult { Success = false, Errors = ["migration failed"] });

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target, false,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(_applied);
    }

    [Fact]
    public async Task Update_CarriesTheCkDependenciesOfTheTargetVersion()
    {
        GivenResolution(Blueprint(Target, "System.Ai-[1.0,2.0)", "Basic-[2.5,3.0)"));

        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge, null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var updated = Assert.Single(_updated);
        Assert.Equal(Target, updated.BlueprintId);
        Assert.Equal(Current, updated.FromVersion);
        Assert.Equal(["Basic", "System.Ai"], updated.CkModelDependencies);
    }

    [Fact]
    public void Notifications_WithoutTheProperty_KeepNullForOlderEngines()
    {
        // The positional constructor is unchanged (binary compatibility): a caller compiled against an older engine
        // creates the record without the dependencies, and consumers must read null as "unknown".
        var applied = new BlueprintAppliedNotification(TenantId, Target, BlueprintApplicationMode.Initial, 0, 0, 0,
            Guid.NewGuid(), DateTime.UtcNow);
        var updated = new BlueprintUpdatedNotification(TenantId, Target, Current, BlueprintUpdateMode.Merge, 0, 0, 0,
            Guid.NewGuid(), DateTime.UtcNow);

        Assert.Null(applied.CkModelDependencies);
        Assert.Null(updated.CkModelDependencies);
    }
}
