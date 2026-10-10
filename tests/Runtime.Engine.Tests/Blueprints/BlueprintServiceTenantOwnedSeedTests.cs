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
/// AB#6383 - a seed entity with <c>rtBlueprintLocked: false</c> belongs to the tenant after the first
/// install: created once, never updated, never re-created after the tenant deleted it, and no
/// conflict. The seed flag decides, not the stamp the tenant copy carries. Entities without the flag
/// keep the product-owned behaviour (renewed by updates, back after a delete).
/// </summary>
public class BlueprintServiceTenantOwnedSeedTests
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

    public BlueprintServiceTenantOwnedSeedTests()
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

    private void GivenCatalogCannotProvide(BlueprintId id) => _unreadable.Add(id.FullName);

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

    private int _tenantIndex;

    /// <summary>A tenant entity of blueprint <paramref name="source" /> carrying an edited payload value.</summary>
    private RtEntity GivenTenantEntity(string key, bool storedLocked, BlueprintId source, int threshold = 99)
    {
        var entity = new RtEntity(RuleType, new OctoObjectId($"bb{++_tenantIndex:x22}"),
            new Dictionary<string, object?>
            {
                ["RtBlueprintSource"] = source.FullName,
                ["RtBlueprintLocked"] = storedLocked,
                ["Threshold"] = threshold
            })
        {
            RtWellKnownName = key
        };
        _tenant.Add(entity);
        return entity;
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

    private Task<BlueprintUpdateResult> Update(BlueprintUpdateMode mode = BlueprintUpdateMode.Merge) =>
        CreateService().ApplyUpdateAsync(TenantId, V2, mode, cancellationToken: TestContext.Current.CancellationToken);

    private Task<BlueprintUpdatePreview> Preview(BlueprintUpdateMode mode = BlueprintUpdateMode.Merge) =>
        CreateService().PreviewUpdateAsync(TenantId, V2, mode, TestContext.Current.CancellationToken);

    private IEnumerable<string?> ImportedKeys => _imported.Select(e => e.RtWellKnownName);

    // ---- first install ----------------------------------------------------------------------

    [Fact]
    public async Task FirstInstall_CreatesTenantOwnedAndProductOwnedEntities()
    {
        GivenSeed(V1, new SeedSpec("owned", false), new SeedSpec("product", null));
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["owned", "product"], ImportedKeys.Order().ToArray());
        Assert.Equal(2, result.EntitiesCreated);
        // The created tenant-owned entity carries the seed flag, so later updates see it as released.
        Assert.Contains(_imported, e => e.RtWellKnownName == "owned"
                                        && e.Attributes.Single(a => a.Id.Equals(LockedAttr)).Value is false);
    }

    // ---- update: the tenant holds the entity ---------------------------------------------------

    [Theory]
    [InlineData(BlueprintUpdateMode.Merge, true)]
    [InlineData(BlueprintUpdateMode.Merge, false)]
    [InlineData(BlueprintUpdateMode.Full, true)]
    [InlineData(BlueprintUpdateMode.Full, false)]
    public async Task Update_TenantHoldsEditedEntity_LeavesAttributesAloneStampsItAndRaisesNoConflict(
        BlueprintUpdateMode mode, bool storedStamp)
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("owned", false, Threshold: 1));
        // Changed seed value: the update must not carry it to the tenant.
        GivenSeed(V2, new SeedSpec("owned", false, Threshold: 5));
        var held = GivenTenantEntity("owned", storedStamp, V1, threshold: 99);

        var result = await Update(mode);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(_imported);
        var stamped = Assert.Single(_stamped);
        Assert.Same(held, stamped);
        Assert.Equal(99, stamped.GetAttributeValueOrDefault<int>("Threshold"));
        Assert.False(stamped.GetAttributeValueOrDefault<bool>("RtBlueprintLocked"));
        Assert.Equal(V2.FullName, stamped.GetAttributeStringValueOrDefault("RtBlueprintSource"));
        Assert.Equal("owned", Assert.Single(result.TenantOwnedSkipped).Key);
        Assert.Empty(result.TenantOwnedStaysDeleted);
    }

    [Fact]
    public async Task Update_TenantHoldsUnchangedEntity_IsSkippedToo()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("owned", false, Threshold: 1));
        GivenSeed(V2, new SeedSpec("owned", false, Threshold: 1));
        GivenTenantEntity("owned", false, V1, threshold: 1);

        var result = await Update();

        Assert.True(result.Success);
        Assert.Empty(_imported);
        Assert.Single(result.TenantOwnedSkipped);
    }

    [Fact]
    public async Task Preview_TenantOwnedEntity_IsListedAsSkippedNotAsConflict()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("owned", false));
        GivenSeed(V2, new SeedSpec("owned", false));
        GivenTenantEntity("owned", false, V1);

        var preview = await Preview();

        Assert.Empty(preview.Conflicts);
        Assert.True(preview.CanProceed);
        var listed = Assert.Single(preview.TenantOwnedSkipped);
        Assert.Equal("owned", listed.Key);
        Assert.NotNull(listed.EntityId);
        Assert.Equal(0, preview.EntitiesToAdd);
        Assert.Equal(0, preview.EntitiesToUpdate);
    }

    [Fact]
    public async Task Update_SafeMode_DoesNotTouchTheTenantOwnedEntityAtAll()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("owned", false));
        GivenSeed(V2, new SeedSpec("owned", false));
        GivenTenantEntity("owned", true, V1);

        var result = await Update(BlueprintUpdateMode.Safe);

        Assert.True(result.Success);
        Assert.Empty(_imported);
        Assert.Empty(_stamped);
    }

    [Fact]
    public async Task Update_TenantEntityOfAnotherSource_IsLeftUntouchedAndNotStamped()
    {
        GivenInstalled(V1);
        GivenSeed(V1);
        GivenSeed(V2, new SeedSpec("owned", false));
        GivenTenantEntity("owned", true, new BlueprintId("Other-1.0.0"));

        var result = await Update();

        Assert.True(result.Success);
        Assert.Empty(_imported);
        Assert.Empty(_stamped);
        Assert.Single(result.TenantOwnedSkipped);
    }

    // ---- update: the tenant lacks the entity -------------------------------------------------

    [Fact]
    public async Task Update_TenantDeletedTenantOwnedEntity_StaysDeleted()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("owned", false));
        GivenSeed(V2, new SeedSpec("owned", false));

        var preview = await Preview();
        var result = await Update();

        Assert.Equal("owned", Assert.Single(preview.TenantOwnedStaysDeleted).Key);
        Assert.Null(preview.TenantOwnedStaysDeleted[0].EntityId);
        Assert.Equal(0, preview.EntitiesToAdd);
        Assert.Empty(preview.Conflicts);
        Assert.True(result.Success);
        Assert.Empty(_imported);
        Assert.Equal("owned", Assert.Single(result.TenantOwnedStaysDeleted).Key);
    }

    [Fact]
    public async Task Update_TenantDeletedProductOwnedEntity_ComesBack()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("product", null), new SeedSpec("flagged-true", true));
        GivenSeed(V2, new SeedSpec("product", null), new SeedSpec("flagged-true", true));

        var result = await Update();

        Assert.True(result.Success);
        Assert.Equal(["flagged-true", "product"], ImportedKeys.Order().ToArray());
        Assert.Equal(2, result.EntitiesAdded);
        Assert.Empty(result.TenantOwnedStaysDeleted);
    }

    [Fact]
    public async Task Update_NewTenantOwnedKeyInTargetSeed_IsCreatedOnce()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("old", null));
        GivenSeed(V2, new SeedSpec("old", null), new SeedSpec("new-default", false));
        GivenTenantEntity("old", true, V1);

        var first = await Update();

        Assert.True(first.Success);
        Assert.Contains("new-default", ImportedKeys);
        Assert.Equal(1, first.EntitiesAdded);

        // The tenant deletes the created entity; the next update (V2 -> V3) finds its key in the
        // previous seed and does not bring it back.
        var v3 = new BlueprintId("Shop-3.0.0");
        GivenSeed(v3, new SeedSpec("old", null), new SeedSpec("new-default", false));
        GivenInstalled(V2);
        _imported.Clear();

        var again = await CreateService().ApplyUpdateAsync(TenantId, v3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(again.Success);
        Assert.DoesNotContain("new-default", ImportedKeys);
        Assert.Equal("new-default", Assert.Single(again.TenantOwnedStaysDeleted).Key);
    }

    [Fact]
    public async Task Update_PreviousSeedNotReadable_AbsentTenantOwnedIsNotCreatedAndWarns()
    {
        GivenInstalled(V1);
        GivenCatalogCannotProvide(V1);
        GivenSeed(V2, new SeedSpec("owned", false), new SeedSpec("product", null));

        var result = await Update();

        Assert.True(result.Success);
        Assert.DoesNotContain("owned", ImportedKeys);
        Assert.Contains("product", ImportedKeys);
        Assert.Contains(result.Warnings, w => w.Contains("could not be read") && w.Contains("owned"));
        Assert.Empty(result.TenantOwnedStaysDeleted);

        var preview = await Preview();
        Assert.Equal(1, preview.EntitiesToAdd); // only the product-owned one
        Assert.Contains(preview.Warnings, w => w.Contains("could not be read"));
    }

    [Fact]
    public async Task Update_PreviousSeedNotReadable_HeldTenantOwnedEntityIsStillSkipped()
    {
        GivenInstalled(V1);
        GivenCatalogCannotProvide(V1);
        GivenSeed(V2, new SeedSpec("owned", false));
        GivenTenantEntity("owned", true, V1);

        var result = await Update();

        Assert.True(result.Success);
        Assert.Empty(_imported);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("could not be read"));
        Assert.Single(result.TenantOwnedSkipped);
    }

    // ---- flag flips between versions ---------------------------------------------------------

    [Fact]
    public async Task FlagFlip_LockedInV1UnlockedInV2_TenantWithOldLockedStampIsHandledAsTenantOwned()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("rule", null));
        GivenSeed(V2, new SeedSpec("rule", false, Threshold: 7));
        var held = GivenTenantEntity("rule", storedLocked: true, V1, threshold: 99);

        var result = await Update();

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(_imported);
        Assert.Equal(99, held.GetAttributeValueOrDefault<int>("Threshold"));
        Assert.False(held.GetAttributeValueOrDefault<bool>("RtBlueprintLocked"));
    }

    [Fact]
    public async Task FlagFlip_LockedInV1UnlockedInV2_DeletedByTenantComesNotBackBecauseKeyWasInV1()
    {
        // The key was product-owned in V1; the tenant deleted it; V2 hands it over to the tenant.
        // "Contained in the previous seed" counts whatever the flag was there.
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("rule", null));
        GivenSeed(V2, new SeedSpec("rule", false));

        var result = await Update();

        Assert.True(result.Success);
        Assert.Empty(_imported);
        Assert.Single(result.TenantOwnedStaysDeleted);
    }

    [Fact]
    public async Task FlagFlip_UnlockedInV1LockedInV2_BehavesAsTodayConflictForTheStampedUnlockedCopy()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("rule", false));
        GivenSeed(V2, new SeedSpec("rule", null));
        GivenTenantEntity("rule", storedLocked: false, V1);

        var preview = await Preview();

        var conflict = Assert.Single(preview.Conflicts);
        Assert.Equal(ConflictType.UserModified, conflict.ConflictType);
        Assert.Empty(preview.TenantOwnedSkipped);
    }

    // ---- product-owned entities are unchanged -----------------------------------------------

    [Fact]
    public async Task Update_HeldProductOwnedEntity_IsStillRenewed()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("product", null, 1));
        GivenSeed(V2, new SeedSpec("product", null, 5));
        GivenTenantEntity("product", true, V1, threshold: 1);

        var result = await Update();

        Assert.True(result.Success);
        Assert.Equal(["product"], ImportedKeys.ToArray());
        Assert.Empty(_stamped);
        Assert.Empty(result.TenantOwnedSkipped);
    }

    [Fact]
    public async Task Update_UnlockedTenantCopyOfProductOwnedSeed_StillBlocksTheUpdate()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("product", null));
        GivenSeed(V2, new SeedSpec("product", null));
        GivenTenantEntity("product", false, V1);

        var result = await Update();

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Update blocked by conflicts"));
    }

    // ---- install path: forced re-apply and install over another version ---------------------

    [Fact]
    public async Task ForcedReapply_HeldTenantOwnedEntity_IsNotOverwritten()
    {
        GivenInstalled(V2);
        GivenSeed(V2, new SeedSpec("owned", false, 5), new SeedSpec("product", null));
        GivenResolution(V2);
        var held = GivenTenantEntity("owned", true, V2, threshold: 99);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V2, true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["product"], ImportedKeys.ToArray());
        Assert.Equal(99, held.GetAttributeValueOrDefault<int>("Threshold"));
        Assert.Same(held, Assert.Single(_stamped));
        Assert.False(held.GetAttributeValueOrDefault<bool>("RtBlueprintLocked"));
        Assert.Contains(result.OperationResult.Messages, m => m.MessageText.Contains("left untouched"));
    }

    [Fact]
    public async Task ForcedReapply_TenantDeletedTenantOwnedEntity_StaysDeletedButProductOwnedReturns()
    {
        GivenInstalled(V2);
        GivenSeed(V2, new SeedSpec("owned", false), new SeedSpec("product", null));
        GivenResolution(V2);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V2, true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["product"], ImportedKeys.ToArray());
        Assert.Contains(result.OperationResult.Messages, m => m.MessageText.Contains("stay deleted"));
    }

    [Fact]
    public async Task InstallOverOtherVersion_PreviousSeedNotReadable_DoesNotCreateAbsentTenantOwned()
    {
        GivenInstalled(V1);
        GivenCatalogCannotProvide(V1);
        GivenSeed(V2, new SeedSpec("owned", false), new SeedSpec("product", null));
        GivenResolution(V2);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V2, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["product"], ImportedKeys.ToArray());
        Assert.Contains(result.OperationResult.Messages,
            m => m.MessageLevel == MessageLevel.Warning && m.MessageText.Contains("not created"));
    }

    [Fact]
    public async Task InstallOverOtherVersion_NewTenantOwnedKey_IsCreated()
    {
        GivenInstalled(V1);
        GivenSeed(V1, new SeedSpec("old", null));
        GivenSeed(V2, new SeedSpec("old", null), new SeedSpec("owned", false));
        GivenResolution(V2);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V2, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["old", "owned"], ImportedKeys.Order().ToArray());
    }
}
