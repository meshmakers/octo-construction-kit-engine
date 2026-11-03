using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
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
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#8256 - a preview of a blueprint that is not installed on the tenant diffs the seed against the tenant's
/// entities by well-known name / rtId (adoption) with the normal output contract, instead of the former placeholder
/// (EntitiesToAdd 1, no changes). An entity the tenant holds is reported as an update (or unchanged), whatever its
/// source; a tenant-only entity is never deleted; tenant-owned seed entities stay skipped.
/// </summary>
public class BlueprintServiceAdoptionPreviewTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId V1 = new("Shop-1.0.0");
    private static readonly BlueprintId V2 = new("Shop-2.0.0");
    private static readonly RtCkId<CkTypeId> RuleType = new("Test-1.0.0/Rule");
    private static readonly RtCkId<CkAttributeId> LockedAttr = new("System/RtBlueprintLocked");
    private static readonly RtCkId<CkAttributeId> ThresholdAttr = new("Test-1.0.0/Threshold");

    /// <summary>One entity of a seed: key, the seed's lock flag (null = absent) and a payload value.</summary>
    private sealed record SeedSpec(string Key, bool? Locked, int Threshold = 1);

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IImportRtModelCommand _import = A.Fake<IImportRtModelCommand>();
    private readonly IRtYamlSerializer _serializer = A.Fake<IRtYamlSerializer>();
    private readonly IBlueprintVariableProvider _variables = A.Fake<IBlueprintVariableProvider>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();

    private readonly Dictionary<string, SeedSpec[]> _seeds = [];
    private readonly HashSet<string> _unreadable = [];
    private readonly List<RtEntity> _tenant = [];
    private readonly List<RtEntityTcDto> _imported = [];
    private readonly List<RtEntity> _stamped = [];

    public BlueprintServiceAdoptionPreviewTests()
    {
        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((BlueprintId id, OperationResult op, object? _, CancellationToken? _) =>
            {
                if (_unreadable.Contains(id.FullName))
                {
                    op.AddMessage(new OperationMessage(MessageLevel.Error, null, 1, $"{id.FullName} is gone"));
                }

                return Task.FromResult(Meta(id));
            });
        A.CallTo(() => _catalog.TryOpenBlueprintFileAsync(A<BlueprintId>._, A<string>._, A<object?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<Stream?>(new MemoryStream()));
        // The description handed to the serializer is "<blueprint full name>/<path>".
        A.CallTo(() => _serializer.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .ReturnsLazily((Stream _, string location, OperationResult _) =>
                Task.FromResult(BuildSeed(_seeds[location[..location.IndexOf('/')]])));

        A.CallTo(() => _variables.GetVariablesAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(_repository));
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(() =>
            {
                var set = A.Fake<IResultSet<RtEntity>>();
                A.CallTo(() => set.Items).Returns(_tenant.ToList());
                return Task.FromResult(set);
            });
        A.CallTo(() => _repository.UpdateOneRtEntityByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<OctoObjectId>._, A<RtEntity>._))
            .Invokes((IOctoSession _, RtCkId<CkTypeId> _, OctoObjectId _, RtEntity e) => _stamped.Add(e))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .Invokes((IRuntimeRepository _, RtModelRootTcDto root, ImportStrategy _, CancellationToken? _,
                RtImportBlankingPolicy _, IReadOnlyCollection<RtImportBlankingConfirmation>? _) =>
                _imported.AddRange(root.Entities))
            .Returns(Task.CompletedTask);
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
        // Not installed unless a test says so (an unconfigured fake would answer with a dummy installation).
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<TenantBlueprintInfo?>(null));
    }

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(),
        _catalog,
        _history,
        A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(),
        A.Fake<ICkModelUpgradeService>(),
        _repositoryProvider,
        _import,
        _serializer,
        A.Fake<IBlueprintNotifications>(),
        _resolver,
        _installations,
        _variables,
        NullLogger<BlueprintService>.Instance);

    // ---- scenario building ------------------------------------------------------------------

    private static BlueprintMetaRootDto Meta(BlueprintId id) => new()
    {
        BlueprintId = id,
        SeedDataPath = "seed.yaml"
    };

    private static RtModelRootTcDto BuildSeed(SeedSpec[] specs)
    {
        var root = new RtModelRootTcDto();
        for (var i = 0; i < specs.Length; i++)
        {
            var entity = new RtEntityTcDto
            {
                RtId = new OctoObjectId($"aa{i + 1:x22}"),
                CkTypeId = RuleType,
                RtWellKnownName = specs[i].Key
            };
            entity.Attributes.Add(new RtAttributeTcDto { Id = ThresholdAttr, Value = specs[i].Threshold });
            if (specs[i].Locked is { } locked)
            {
                entity.Attributes.Add(new RtAttributeTcDto { Id = LockedAttr, Value = locked });
            }

            root.Entities.Add(entity);
        }

        return root;
    }

    private void GivenSeed(BlueprintId id, params SeedSpec[] specs) => _seeds[id.FullName] = specs;

    private int _tenantIndex;

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

    private void GivenResolution(BlueprintId root)
    {
        A.CallTo(() => _resolver.ResolveAsync(root, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = root,
                Success = true,
                InstallOrder = [Meta(root)],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });
    }

    private Task<BlueprintUpdatePreview> Preview(BlueprintUpdateMode mode = BlueprintUpdateMode.Merge) =>
        CreateService().PreviewUpdateAsync(TenantId, V2, mode, TestContext.Current.CancellationToken);

    private IEnumerable<string?> ImportedKeys => _imported.Select(e => e.RtWellKnownName);

    /// <summary>
    /// A tenant entity created outside the blueprint (manual import with the seed's rtId): no blueprint source, no lock
    /// stamp. <paramref name="seedIndex" /> is the 1-based position of the seed entity whose rtId it carries
    /// (<see cref="BuildSeed" />), 0 for an rtId the seed does not use.
    /// </summary>
    private RtEntity GivenUnmanagedTenantEntity(string key, int seedIndex, int threshold = 99, string? source = null)
    {
        var attributes = new Dictionary<string, object?> { ["Threshold"] = threshold };
        if (source != null)
        {
            attributes["RtBlueprintSource"] = source;
        }

        var rtId = seedIndex > 0 ? $"aa{seedIndex:x22}" : $"cc{++_tenantIndex:x22}";
        var entity = new RtEntity(RuleType, new OctoObjectId(rtId), attributes) { RtWellKnownName = key };
        _tenant.Add(entity);
        return entity;
    }

    // ---- preview without an installation --------------------------------------------------------

    [Fact]
    public async Task Preview_NotInstalled_DiffsTheSeedAgainstTheTenant_InsteadOfAPlaceholder()
    {
        GivenSeed(V2, new SeedSpec("held", null), new SeedSpec("new-1", null), new SeedSpec("new-2", null));
        var held = GivenUnmanagedTenantEntity("held", 1);

        var preview = await Preview();

        Assert.Equal(2, preview.EntitiesToAdd);
        // The test cache knows no CK type, so the held entity is reported as an update with a note (over-reporting
        // is the documented failure direction); with a cached type only changed attributes count.
        Assert.Equal(1, preview.EntitiesToUpdate);
        Assert.Equal(held.RtId.ToString(), Assert.Single(preview.Changes).EntityId);
        Assert.Equal(0, preview.EntitiesToDelete);
        Assert.Empty(preview.Conflicts);
        Assert.True(preview.CanProceed);
        Assert.Contains(preview.Warnings, w => w.Contains("is not installed on the tenant") && w.Contains("adoption"));
        Assert.Contains(preview.Warnings, w => w.StartsWith("1 seed entities already exist in the tenant"));
        Assert.DoesNotContain(preview.Warnings, w => w.Contains("fresh application"));
    }

    [Fact]
    public async Task Preview_NotInstalled_MatchesByRtId_AlsoWhenTheTenantRenamedTheEntity()
    {
        GivenSeed(V2, new SeedSpec("rule", null));
        GivenUnmanagedTenantEntity("renamed", 1);

        var preview = await Preview();

        Assert.Equal(0, preview.EntitiesToAdd);
        Assert.Equal(1, preview.EntitiesToUpdate);
    }

    [Fact]
    public async Task Preview_NotInstalled_SameWellKnownNameOtherRtId_IsAnAdd_AndWarned()
    {
        GivenSeed(V2, new SeedSpec("rule", null));
        var other = GivenUnmanagedTenantEntity("rule", 0);

        var preview = await Preview();

        Assert.Equal(1, preview.EntitiesToAdd);
        Assert.Equal(0, preview.EntitiesToUpdate);
        Assert.Contains(preview.Warnings, w => w.Contains("well-known name") && w.Contains(other.RtId.ToString()));
    }

    [Fact]
    public async Task Preview_NotInstalled_NamesEntitiesThatCarryAnotherBlueprintsSource()
    {
        GivenSeed(V2, new SeedSpec("shared", null));
        GivenUnmanagedTenantEntity("shared", 1, source: "Other-1.0.0");

        var preview = await Preview();

        Assert.Equal(1, preview.EntitiesToUpdate);
        Assert.Contains(preview.Warnings, w => w.Contains("another blueprint") && w.Contains("Other-1.0.0"));
    }

    [Fact]
    public async Task Preview_NotInstalled_FullMode_NeverDeletesTenantOnlyEntities()
    {
        GivenSeed(V2, new SeedSpec("seeded", null));
        GivenUnmanagedTenantEntity("seeded", 1);
        GivenUnmanagedTenantEntity("tenant-only", 0);

        var preview = await Preview(BlueprintUpdateMode.Full);

        Assert.Equal(0, preview.EntitiesToDelete);
        Assert.Equal(0, preview.EntitiesToAdd);
        Assert.Equal(1, preview.EntitiesToUpdate);
    }

    [Fact]
    public async Task Preview_NotInstalled_SafeMode_ReportsOnlyTheAdds()
    {
        GivenSeed(V2, new SeedSpec("held", null), new SeedSpec("new", null));
        GivenUnmanagedTenantEntity("held", 1);

        var preview = await Preview(BlueprintUpdateMode.Safe);

        Assert.Equal(1, preview.EntitiesToAdd);
        Assert.Equal(0, preview.EntitiesToUpdate);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public async Task Preview_NotInstalled_HeldTenantOwnedSeedEntity_IsSkipped()
    {
        GivenSeed(V2, new SeedSpec("owned", false), new SeedSpec("product", null));
        GivenUnmanagedTenantEntity("owned", 1);

        var preview = await Preview();

        Assert.Equal("owned", Assert.Single(preview.TenantOwnedSkipped).Key);
        Assert.Equal(1, preview.EntitiesToAdd);
        Assert.Equal(0, preview.EntitiesToUpdate);
    }

    [Fact]
    public async Task Preview_NotInstalled_EmptyTenant_CountsEverySeedEntityAsAdd()
    {
        GivenSeed(V2, new SeedSpec("a", null), new SeedSpec("b", null), new SeedSpec("c", null));

        var preview = await Preview();

        Assert.Equal(3, preview.EntitiesToAdd);
        Assert.Equal(0, preview.EntitiesToUpdate);
        Assert.DoesNotContain(preview.Warnings, w => w.Contains("already exist in the tenant"));
    }

    [Fact]
    public async Task Preview_NotInstalled_MigrationMode_StillReportsTheMissingScript()
    {
        GivenSeed(V2, new SeedSpec("a", null));

        var preview = await Preview(BlueprintUpdateMode.Migration);

        Assert.Equal(ConflictType.MissingMigrationScript, Assert.Single(preview.Conflicts).ConflictType);
        Assert.Equal(0, preview.EntitiesToAdd);
    }

    [Fact]
    public async Task Preview_Installed_IsUnchanged_EntitiesWithoutTheBlueprintSourceAreNotAdopted()
    {
        GivenInstalled(V1);
        GivenSeed(V1);
        GivenSeed(V2, new SeedSpec("held", null));
        GivenUnmanagedTenantEntity("held", 1);

        var preview = await Preview();

        Assert.Equal(1, preview.EntitiesToAdd);
        Assert.Equal(0, preview.EntitiesToUpdate);
        Assert.DoesNotContain(preview.Warnings, w => w.Contains("already exist in the tenant"));
    }

    // ---- install into a tenant that holds the entities ----------------------------------------------

    [Fact]
    public async Task Install_NotInstalled_UpsertsExactlyWhatThePreviewListed_AndRecordsTheInstallation()
    {
        GivenSeed(V2, new SeedSpec("held", null), new SeedSpec("new", null), new SeedSpec("owned", false));
        GivenUnmanagedTenantEntity("held", 1);
        GivenUnmanagedTenantEntity("owned", 3);
        GivenUnmanagedTenantEntity("tenant-only", 0);
        GivenResolution(V2);

        var preview = await Preview();
        var result = await CreateService().ApplyBlueprintAsync(TenantId, V2, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        // Preview: 1 add (new), 1 update (held), tenant-owned "owned" skipped, "tenant-only" untouched.
        Assert.Equal(1, preview.EntitiesToAdd);
        Assert.Equal(1, preview.EntitiesToUpdate);
        Assert.Equal("owned", Assert.Single(preview.TenantOwnedSkipped).Key);
        Assert.Equal(0, preview.EntitiesToDelete);
        // Install: the same entities, by upsert (the held one keeps its rtId), nothing else.
        Assert.Equal(["held", "new"], ImportedKeys.Order().ToArray());
        A.CallTo(() => _repository.DeleteOneRtEntityByRtIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
            A<OctoObjectId>._, A<DeleteOptions>._)).MustNotHaveHappened();
    }
}
