using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#7991 - <see cref="BlueprintService" /> activates the archives a seed creates on install and update, after the
/// import, and reports every archive in the result. Re-applies (also forced) do not touch archives the tenant holds.
/// The ordering, guard and failure rules are covered by <see cref="BlueprintArchiveActivatorTests" />.
/// </summary>
public class BlueprintServiceArchiveActivationTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId V1 = new("Solution-1.0.0");
    private static readonly BlueprintId V2 = new("Solution-1.1.0");
    private static readonly RtCkId<CkTypeId> RawType = new("System.StreamData/RawArchive");
    private static readonly RtCkId<CkTypeId> RuleType = new("Test-1.0.0/Rule");
    private static readonly OctoObjectId Raw = new("6a0e000000000000000a0007");
    private static readonly OctoObjectId Raw2 = new("6a0e000000000000000a0008");
    private static readonly OctoObjectId Rule = new("6a0e000000000000000c0001");

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IImportRtModelCommand _import = A.Fake<IImportRtModelCommand>();
    private readonly IRtYamlSerializer _serializer = A.Fake<IRtYamlSerializer>();
    private readonly IBlueprintVariableProvider _variables = A.Fake<IBlueprintVariableProvider>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly ICkCacheService _cache = A.Fake<ICkCacheService>();
    private readonly IStreamDataTenantServicesProvider _streamData = A.Fake<IStreamDataTenantServicesProvider>();
    private readonly IArchiveLifecycleService _lifecycle = A.Fake<IArchiveLifecycleService>();
    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();

    private readonly Dictionary<string, OctoObjectId[]> _seeds = [];
    private readonly List<RtEntity> _tenant = [];
    private readonly List<OctoObjectId> _activated = [];
    private bool _importFails;

    public BlueprintServiceArchiveActivationTests()
    {
        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((BlueprintId id, OperationResult _, object? _, CancellationToken? _) => Task.FromResult(Meta(id)));
        A.CallTo(() => _catalog.TryOpenBlueprintFileAsync(A<BlueprintId>._, A<string>._, A<object?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<Stream?>(new MemoryStream()));
        A.CallTo(() => _serializer.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .ReturnsLazily((Stream _, string location, OperationResult _) =>
                Task.FromResult(BuildSeed(_seeds[location[..location.IndexOf('/')]])));
        A.CallTo(() => _variables.GetVariablesAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(_repository));
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, RtCkId<CkTypeId> type, RtEntityQueryOptions _, int? _, int? _) =>
                Task.FromResult(ResultSet(_tenant.Where(e => type.Equals(e.CkTypeId)))));
        A.CallTo(() => _repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, RtCkId<CkTypeId> type, IReadOnlyList<OctoObjectId> ids,
                    RtEntityQueryOptions _, int? _, int? _) =>
                Task.FromResult(ResultSet(_tenant.Where(e => type.Equals(e.CkTypeId) && ids.Contains(e.RtId)))));
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .ReturnsLazily(() => _importFails
                ? throw new InvalidOperationException("import refused")
                : Task.CompletedTask);
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));

        // Only the archive type is an archive for ArchiveDefinitionGuard.IsArchive.
        CkTypeGraph? archiveGraph = ArchiveTypeGraph();
        A.CallTo(() => _cache.TryGetRtCkType(TenantId, RawType, out archiveGraph)).Returns(true);

        A.CallTo(() => _streamData.GetAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<StreamDataTenantServices?>(
                new StreamDataTenantServices(true, _lifecycle, _archiveStore, null, null)));
        A.CallTo(() => _archiveStore.GetAsync(A<OctoObjectId>._)).ReturnsLazily((OctoObjectId id) =>
            Task.FromResult<ArchiveSnapshot?>(new ArchiveSnapshot(id, new RtCkId<CkTypeId>("Test-1.0.0/Meter"),
                _activated.Contains(id) ? CkArchiveStatus.Activated : CkArchiveStatus.Created, null, [])));
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._))
            .Invokes((OctoObjectId id) => _activated.Add(id)).Returns(Task.CompletedTask);
    }

    private static CkTypeGraph ArchiveTypeGraph() => new(
        new CkId<CkTypeId>("System.StreamData-1.0.0/Archive"),
        isAbstract: false,
        isFinal: false,
        isCollectionRoot: false,
        baseTypes: [],
        derivedFromCkTypeId: null,
        definingCollectionRootCkTypeId: null,
        derivedTypes: [],
        definedAttributes: [],
        allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(),
        indexes: [],
        associations: new CkGraphDirectedAssociations([]),
        description: "test",
        enableChangeStreamPreAndPostImages: false);

    private static IResultSet<RtEntity> ResultSet(IEnumerable<RtEntity> items)
    {
        var set = A.Fake<IResultSet<RtEntity>>();
        A.CallTo(() => set.Items).Returns(items.ToList());
        return set;
    }

    private BlueprintService CreateService(bool withStreamData = true) => new(
        _cache, _catalog, _history, A.Fake<IBlueprintMigrationExecutor>(), A.Fake<IBlueprintMigrationParser>(),
        A.Fake<ICkModelUpgradeService>(), _repositoryProvider, _import, _serializer, A.Fake<IBlueprintNotifications>(),
        _resolver, _installations, _variables, NullLogger<BlueprintService>.Instance, null,
        withStreamData ? _streamData : null);

    private BlueprintMetaRootDto Meta(BlueprintId id) => new()
    {
        BlueprintId = id,
        SeedDataPath = "seed.yaml",
        ArchiveActivation = _exclude.Count == 0 ? null : new BlueprintArchiveActivationDto { Exclude = _exclude }
    };

    private readonly List<string> _exclude = [];

    private static RtModelRootTcDto BuildSeed(OctoObjectId[] ids)
    {
        var root = new RtModelRootTcDto();
        foreach (var id in ids)
        {
            root.Entities.Add(new RtEntityTcDto
            {
                RtId = id,
                CkTypeId = id.Equals(Rule) ? RuleType : RawType,
                RtWellKnownName = $"e-{id}"
            });
        }

        return root;
    }

    private void GivenResolution(BlueprintId root) =>
        A.CallTo(() => _resolver.ResolveAsync(root, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = root,
                Success = true,
                InstallOrder = [Meta(root)],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });

    private void GivenInstalled(BlueprintId id)
    {
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(TenantId, id.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<TenantBlueprintInfo?>(new TenantBlueprintInfo
            {
                BlueprintId = id,
                AppliedAt = DateTime.UtcNow
            }));
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, id.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(new BlueprintInstallation
            {
                BlueprintId = id,
                InstalledAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                IsDependency = false
            }));
    }

    private void GivenTenantHolds(OctoObjectId id, BlueprintId source) =>
        _tenant.Add(new RtEntity(RawType, id, new Dictionary<string, object?>
        {
            ["RtBlueprintSource"] = source.FullName,
            ["RtBlueprintLocked"] = true
        })
        {
            RtWellKnownName = $"e-{id}"
        });

    // ---- install -----------------------------------------------------------------------------

    [Fact]
    public async Task Install_ActivatesTheNewArchives_AfterTheImport_AndListsThem()
    {
        _seeds[V1.FullName] = [Raw, Raw2, Rule];
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([Raw, Raw2], _activated);
        Assert.Equal(2, result.ArchiveActivations.Count); // the non-archive entity is not listed
        Assert.All(result.ArchiveActivations, a =>
        {
            Assert.Equal(BlueprintArchiveActivationOutcome.Activated, a.Outcome);
            Assert.Equal(V1.FullName, a.BlueprintId);
        });
        Assert.Contains(result.OperationResult.Messages, m => m.MessageNumber == 70 && m.MessageText.Contains("2 activated"));
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._, A<ImportStrategy>._,
                A<CancellationToken?>._, A<RtImportBlankingPolicy>._, A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _lifecycle.ActivateAsync(Raw)).MustHaveHappened());
    }

    [Fact]
    public async Task ForcedReApply_DoesNotReActivate_ArchivesTheTenantHolds()
    {
        _seeds[V1.FullName] = [Raw];
        GivenResolution(V1);
        GivenInstalled(V1);
        GivenTenantHolds(Raw, V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(_activated);
        Assert.Equal(BlueprintArchiveActivationOutcome.SkippedPreExisting, Assert.Single(result.ArchiveActivations).Outcome);
        A.CallTo(() => _streamData.GetAsync(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ReApplyWithoutForce_ImportsNothing_AndActivatesNothing()
    {
        _seeds[V1.FullName] = [Raw];
        GivenResolution(V1);
        GivenInstalled(V1);
        GivenTenantHolds(Raw, V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ArchiveActivations);
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Install_OptedOutArchive_StaysCreated()
    {
        _seeds[V1.FullName] = [Raw, Raw2];
        _exclude.Add($"e-{Raw2}");
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([Raw], _activated);
        Assert.Equal(BlueprintArchiveActivationOutcome.SkippedOptOut,
            result.ArchiveActivations.Single(a => a.ArchiveRtId == Raw2.ToString()).Outcome);
    }

    [Fact]
    public async Task Install_WithoutAStreamDataBackend_Succeeds_ArchivesStayCreated_WithAWarning()
    {
        _seeds[V1.FullName] = [Raw];
        GivenResolution(V1);

        var result = await CreateService(withStreamData: false)
            .ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(BlueprintArchiveActivationOutcome.SkippedStreamDataDisabled,
            Assert.Single(result.ArchiveActivations).Outcome);
        Assert.Contains(result.OperationResult.Messages,
            m => m.MessageNumber == 71 && m.MessageLevel == MessageLevel.Warning);
    }

    [Fact]
    public async Task Install_FailedImport_ActivatesNothing()
    {
        _seeds[V1.FullName] = [Raw];
        GivenResolution(V1);
        _importFails = true;

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._)).MustNotHaveHappened();
    }

    // ---- update ------------------------------------------------------------------------------

    [Fact]
    public async Task Update_AddingAnArchive_ActivatesOnlyTheNewOne()
    {
        GivenInstalled(V1);
        _seeds[V1.FullName] = [Raw];
        _seeds[V2.FullName] = [Raw, Raw2];
        GivenTenantHolds(Raw, V1); // left Created on purpose: must stay so

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal([Raw2], _activated);
        var added = Assert.Single(result.ArchiveActivations, a => a.ArchiveRtId == Raw2.ToString());
        Assert.Equal(BlueprintArchiveActivationOutcome.Activated, added.Outcome);
        Assert.DoesNotContain(result.ArchiveActivations,
            a => a.ArchiveRtId == Raw.ToString() && a.Outcome != BlueprintArchiveActivationOutcome.SkippedPreExisting);
    }

    [Fact]
    public async Task Update_DryRun_ActivatesNothing()
    {
        GivenInstalled(V1);
        _seeds[V1.FullName] = [Raw];
        _seeds[V2.FullName] = [Raw, Raw2];
        GivenTenantHolds(Raw, V1);

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { DryRun = true }, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.ArchiveActivations);
        A.CallTo(() => _lifecycle.ActivateAsync(A<OctoObjectId>._)).MustNotHaveHappened();
    }

    [Fact]
    public void DescribeArchiveActivations_NamesTheQueuedJobs()
    {
        var text = BlueprintService.DescribeArchiveActivations(
        [
            new BlueprintArchiveActivation
            {
                ArchiveRtId = Raw.ToString(), CkTypeId = RawType.ToString(), BlueprintId = V1.FullName,
                Outcome = BlueprintArchiveActivationOutcome.Activated, WellKnownName = "GridRollup1h",
                Backfill = BlueprintRollupBackfillOutcome.Queued, BackfillJobRtId = "job-1"
            }
        ]);

        Assert.Contains("1 activated", text);
        Assert.Contains("1 rollup backfill(s) queued (GridRollup1h: job job-1)", text);
    }
}
