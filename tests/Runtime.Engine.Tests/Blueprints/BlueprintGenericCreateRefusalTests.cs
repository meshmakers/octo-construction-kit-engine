using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
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
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
///     A blueprint (seed data of an install, the diff of an update, the Add step of a migration) never runs a CK
///     method, so a NEW entity of a type whose static method declares <c>replacesGenericCreate</c> (or of a type
///     derived from it) is refused before anything is written. An entity whose rtId exists is upserted as before.
///     The tests use a self-made type graph; they depend on no System version.
/// </summary>
public class BlueprintGenericCreateRefusalTests
{
    private const string TenantId = "tenant-a";
    private const string MethodName = "Create-1";
    private static readonly BlueprintId V1 = new("Shop-1.0.0");
    private static readonly BlueprintId V2 = new("Shop-2.0.0");
    private static readonly RtCkId<CkTypeId> AccountType = new("Test-1.0.0/Account");
    private static readonly RtCkId<CkTypeId> SpecialAccountType = new("Test-1.0.0/SpecialAccount");
    private static readonly RtCkId<CkTypeId> PlainType = new("Test-1.0.0/Plain");
    private static readonly RtCkId<CkAttributeId> LockedAttr = new("System/RtBlueprintLocked");
    private static readonly RtCkId<CkAttributeId> ThresholdAttr = new("Test-1.0.0/Threshold");

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IImportRtModelCommand _import = A.Fake<IImportRtModelCommand>();
    private readonly IRtYamlSerializer _serializer = A.Fake<IRtYamlSerializer>();
    private readonly IBlueprintVariableProvider _variables = A.Fake<IBlueprintVariableProvider>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly ICkCacheService _ckCache = A.Fake<ICkCacheService>();

    private readonly Dictionary<string, RtModelRootTcDto> _seeds = [];
    private readonly List<RtEntity> _tenant = [];
    private readonly List<RtEntityTcDto> _imported = [];

    public BlueprintGenericCreateRefusalTests()
    {
        // Account declares the replacing method, SpecialAccount derives (inherits it), Plain has none.
        var method = new CkMethodGraph(new CkId<CkTypeId>("Test-1.0.0/Account"),
            new CkMethodDto { MethodId = MethodName, Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true });
        Cache(AccountType, method);
        Cache(SpecialAccountType, method);
        Cache(PlainType);

        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((BlueprintId id, OperationResult _, object? _, CancellationToken? _) =>
                Task.FromResult(Meta(id)));
        A.CallTo(() => _catalog.TryOpenBlueprintFileAsync(A<BlueprintId>._, A<string>._, A<object?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<Stream?>(new MemoryStream()));
        A.CallTo(() => _serializer.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .ReturnsLazily((Stream _, string location, OperationResult _) =>
                Task.FromResult(_seeds[location[..location.IndexOf('/')]]));
        A.CallTo(() => _variables.GetVariablesAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
        A.CallTo(() => _repository.TenantId).Returns(TenantId);
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(_repository));
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, RtCkId<CkTypeId> type, RtEntityQueryOptions _, int? _, int? _) =>
                Result(_tenant.Where(e => e.CkTypeId!.Equals(type))));
        A.CallTo(() => _repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, RtCkId<CkTypeId> type, IReadOnlyList<OctoObjectId> ids,
                RtEntityQueryOptions _, int? _, int? _) =>
                Result(_tenant.Where(e => e.CkTypeId!.Equals(type) && ids.Contains(e.RtId))));
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

    private static Task<IResultSet<RtEntity>> Result(IEnumerable<RtEntity> items)
    {
        var set = A.Fake<IResultSet<RtEntity>>();
        A.CallTo(() => set.Items).Returns(items.ToList());
        return Task.FromResult(set);
    }

    private void Cache(RtCkId<CkTypeId> rtTypeId, CkMethodGraph? method = null)
    {
        var graph = new CkTypeGraph(new CkId<CkTypeId>(rtTypeId.SemanticVersionedFullName),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(), indexes: [],
            associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false,
            allMethods: method == null
                ? null
                : new Dictionary<string, CkMethodGraph> { [method.Definition.MethodId] = method });
        A.CallTo(() => _ckCache.TryGetRtCkType(TenantId, rtTypeId, out graph)).Returns(true);
    }

    private BlueprintService CreateService() => new(
        _ckCache,
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

    private static BlueprintMetaRootDto Meta(BlueprintId id) => new() { BlueprintId = id, SeedDataPath = "seed.yaml" };

    private static RtEntityTcDto SeedEntity(RtCkId<CkTypeId> type, string rtId, string key, int threshold = 1)
    {
        var entity = new RtEntityTcDto { RtId = new OctoObjectId(rtId), CkTypeId = type, RtWellKnownName = key };
        entity.Attributes.Add(new RtAttributeTcDto { Id = ThresholdAttr, Value = threshold });
        return entity;
    }

    private void GivenSeed(BlueprintId id, params RtEntityTcDto[] entities) =>
        _seeds[id.FullName] = new RtModelRootTcDto { Entities = entities.ToList() };

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

    private RtEntity GivenTenantEntity(RtCkId<CkTypeId> type, string rtId, string key, BlueprintId source,
        int threshold = 99)
    {
        var entity = new RtEntity(type, new OctoObjectId(rtId), new Dictionary<string, object?>
        {
            ["RtBlueprintSource"] = source.FullName,
            ["RtBlueprintLocked"] = true,
            ["Threshold"] = threshold
        })
        {
            RtWellKnownName = key
        };
        _tenant.Add(entity);
        return entity;
    }

    private static string Id(int n) => $"aa{n:x22}";

    // ---- install ----------------------------------------------------------------------------

    [Fact]
    public async Task Install_NewEntitiesOfFlaggedAndDerivedType_AreRefusedBeforeAnythingIsWritten()
    {
        GivenSeed(V1, SeedEntity(AccountType, Id(1), "acc"), SeedEntity(SpecialAccountType, Id(2), "special"),
            SeedEntity(PlainType, Id(3), "plain"));
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(_imported);
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._, A<ImportStrategy>._,
            A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
            A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._)).MustNotHaveHappened();
        A.CallTo(() => _installations.UpsertAsync(A<string>._, A<BlueprintInstallation>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _history.AddEntryAsync(A<string>._, A<TenantBlueprintInfo>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        var refusals = result.OperationResult.Messages
            .Where(m => m.MessageNumber == BlueprintGenericCreateGuard.MessageNumber).ToList();
        Assert.Equal(2, refusals.Count);
        var account = Assert.Single(refusals, m => m.MessageText.Contains(Id(1)));
        Assert.Contains("'Test-1.0.0/Account'", account.MessageText);
        Assert.Contains($"'Test/Account.{MethodName}'", account.MessageText);
        Assert.Contains(GenericCreateReplacement.ReasonToken, account.MessageText);
        Assert.Equal(MessageLevel.Error, account.MessageLevel);
        // The derived concrete type is refused with the inherited method (declared on Account).
        var special = Assert.Single(refusals, m => m.MessageText.Contains(Id(2)));
        Assert.Contains("'Test-1.0.0/SpecialAccount'", special.MessageText);
        Assert.Contains($"'Test/Account.{MethodName}'", special.MessageText);
    }

    [Fact]
    public async Task Install_EntityOfFlaggedTypeWhoseRtIdExists_IsUpsertedAsBefore()
    {
        GivenTenantEntity(AccountType, Id(1), "acc", V1);
        GivenSeed(V1, SeedEntity(AccountType, Id(1), "acc", threshold: 5));
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.OperationResult.Messages.Select(m => m.MessageText)));
        Assert.Equal("acc", Assert.Single(_imported).RtWellKnownName);
    }

    [Fact]
    public async Task Install_WithoutFlaggedTypes_InstallsAsBefore()
    {
        GivenSeed(V1, SeedEntity(PlainType, Id(3), "plain"), SeedEntity(PlainType, Id(4), "plain2"));
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _imported.Count);
        Assert.Equal(2, result.EntitiesCreated);
        // Types without a replacing method cost no read of the stored entities.
        A.CallTo(() => _repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
            A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
    }

    // ---- update -----------------------------------------------------------------------------

    [Theory]
    [InlineData(BlueprintUpdateMode.Safe)]
    [InlineData(BlueprintUpdateMode.Merge)]
    [InlineData(BlueprintUpdateMode.Full)]
    public async Task Update_NewEntityOfFlaggedType_IsRefusedAndNothingIsImported(BlueprintUpdateMode mode)
    {
        GivenInstalled(V1);
        GivenSeed(V1, SeedEntity(PlainType, Id(3), "plain"));
        GivenSeed(V2, SeedEntity(PlainType, Id(3), "plain"), SeedEntity(AccountType, Id(1), "acc"),
            SeedEntity(SpecialAccountType, Id(2), "special"));
        GivenTenantEntity(PlainType, Id(3), "plain", V1);

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, mode,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_imported);
        Assert.Equal(2, result.Errors.Count(e => e.StartsWith("OCTO-CK136:", StringComparison.Ordinal)));
        Assert.Contains(result.Errors, e => e.Contains(Id(1)) && e.Contains("'Test-1.0.0/Account'")
                                                                && e.Contains(GenericCreateReplacement.ReasonToken));
        Assert.Contains(result.Errors, e => e.Contains(Id(2)) && e.Contains("'Test-1.0.0/SpecialAccount'"));
    }

    [Fact]
    public async Task Update_ContinueOnError_DoesNotSoftenTheRefusal()
    {
        GivenInstalled(V1);
        GivenSeed(V1, SeedEntity(PlainType, Id(3), "plain"));
        GivenSeed(V2, SeedEntity(PlainType, Id(3), "plain", threshold: 7), SeedEntity(AccountType, Id(1), "acc"));
        GivenTenantEntity(PlainType, Id(3), "plain", V1, threshold: 1);

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { ContinueOnError = true },
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_imported);
    }

    [Fact]
    public async Task Update_EntityOfFlaggedTypeWhoseRtIdExists_IsUpsertedAsBefore()
    {
        GivenInstalled(V1);
        GivenSeed(V1, SeedEntity(AccountType, Id(1), "acc", threshold: 1));
        GivenSeed(V2, SeedEntity(AccountType, Id(1), "acc", threshold: 5));
        GivenTenantEntity(AccountType, Id(1), "acc", V1, threshold: 1);

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("acc", Assert.Single(_imported).RtWellKnownName);
    }

    [Fact]
    public async Task Update_WithoutFlaggedTypes_AddsAsBefore()
    {
        GivenInstalled(V1);
        GivenSeed(V1, SeedEntity(PlainType, Id(3), "plain"));
        GivenSeed(V2, SeedEntity(PlainType, Id(3), "plain"), SeedEntity(PlainType, Id(4), "plain2"));
        GivenTenantEntity(PlainType, Id(3), "plain", V1);

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Safe,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("plain2", Assert.Single(_imported).RtWellKnownName);
    }

    // ---- migration Add step -----------------------------------------------------------------

    private BlueprintMigrationExecutor CreateExecutor() => new(_repositoryProvider, _import, _ckCache,
        NullLogger<BlueprintMigrationExecutor>.Instance);

    private static BlueprintMigrationDto AddStep(RtCkId<CkTypeId> type, string rtId)
    {
        var data = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["rtId"] = rtId,
            ["ckTypeId"] = type.SemanticVersionedFullName
        });
        return new BlueprintMigrationDto
        {
            SourceVersion = V1.FullName,
            TargetVersion = V2.FullName,
            Steps =
            [
                new MigrationStepDto
                {
                    StepId = "add-1",
                    Action = MigrationActionType.Add,
                    Target = new EntityTargetDto { CkTypeId = type.SemanticVersionedFullName },
                    Data = data
                }
            ]
        };
    }

    [Theory]
    [InlineData("Test-1.0.0/Account")]
    [InlineData("Test-1.0.0/SpecialAccount")]
    public async Task MigrationAdd_NewEntityOfFlaggedType_FailsTheStepAndWritesNothing(string type)
    {
        var result = await CreateExecutor().ExecuteAsync(TenantId, AddStep(new RtCkId<CkTypeId>(type), Id(1)),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_imported);
        var error = Assert.Single(result.Errors);
        Assert.Contains("OCTO-CK136:", error);
        Assert.Contains(Id(1), error);
        Assert.Contains($"'{type}'", error);
        Assert.Contains($"'Test/Account.{MethodName}'", error);
        Assert.Contains(GenericCreateReplacement.ReasonToken, error);
    }

    [Fact]
    public async Task MigrationAdd_EntityOfFlaggedTypeWhoseRtIdExists_IsUpsertedAsBefore()
    {
        GivenTenantEntity(AccountType, Id(1), "acc", V1);

        var result = await CreateExecutor().ExecuteAsync(TenantId, AddStep(AccountType, Id(1)),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(_imported);
    }

    [Fact]
    public async Task MigrationAdd_WithoutFlaggedType_AddsAsBefore()
    {
        var result = await CreateExecutor().ExecuteAsync(TenantId, AddStep(PlainType, Id(3)),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(_imported);
    }
}

/// <summary>The public lookup "which method replaces the generic create of type X" (own and inherited).</summary>
public class GenericCreateReplacementTests
{
    private static CkTypeGraph Graph(string id, params CkMethodGraph[] methods) => new(new CkId<CkTypeId>(id),
        isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
        derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
        allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(), indexes: [],
        associations: new CkGraphDirectedAssociations([]), description: "test",
        enableChangeStreamPreAndPostImages: false,
        allMethods: methods.ToDictionary(m => m.Definition.MethodId));

    private static CkMethodGraph Method(string declaring, string id, bool replaces) =>
        new(new CkId<CkTypeId>(declaring),
            new CkMethodDto { MethodId = id, Kind = CkMethodKindDto.Static, ReplacesGenericCreate = replaces });

    [Fact]
    public void FindReplacingMethod_ReturnsTheFlaggedMethodAndIgnoresOthers()
    {
        var graph = Graph("Test-1.0.0/Account", Method("Test-1.0.0/Account", "Other-1", false),
            Method("Test-1.0.0/Account", "Create-1", true));

        var method = GenericCreateReplacement.FindReplacingMethod(graph);

        Assert.NotNull(method);
        Assert.Equal("Create-1", method.Definition.MethodId);
        Assert.Equal("Test/Account.Create-1", method.QualifiedMethodId);
    }

    [Fact]
    public void FindReplacingMethod_InheritedMethod_ReportsTheDeclaringBaseType()
    {
        var derived = Graph("Test-1.0.0/Special", Method("Test-1.0.0/Account", "Create-1", true));

        var method = GenericCreateReplacement.FindReplacingMethod(derived);

        Assert.NotNull(method);
        Assert.Equal("Test/Account.Create-1", method.QualifiedMethodId);
    }

    [Fact]
    public void FindReplacingMethod_NoFlaggedMethod_ReturnsNull()
    {
        Assert.Null(GenericCreateReplacement.FindReplacingMethod(
            Graph("Test-1.0.0/Plain", Method("Test-1.0.0/Plain", "Do-1", false))));
        Assert.Null(GenericCreateReplacement.FindReplacingMethod(Graph("Test-1.0.0/Empty")));
    }

    [Fact]
    public void TryGetReplacingMethod_UnknownTypeIsNotRefused()
    {
        var cache = A.Fake<ICkCacheService>();

        Assert.False(GenericCreateReplacement.TryGetReplacingMethod(cache, "t", new RtCkId<CkTypeId>("Test-1.0.0/Nope"),
            out var method));
        Assert.Null(method);
    }

    [Fact]
    public void TryGetReplacingMethod_UsesTheTenantCache()
    {
        var cache = A.Fake<ICkCacheService>();
        var graph = Graph("Test-1.0.0/Account", Method("Test-1.0.0/Account", "Create-1", true));
        var rtId = new RtCkId<CkTypeId>("Test-1.0.0/Account");
        A.CallTo(() => cache.TryGetRtCkType("t", rtId, out graph)).Returns(true);

        Assert.True(GenericCreateReplacement.TryGetReplacingMethod(cache, "t", rtId, out var method));
        Assert.Equal("Test/Account.Create-1", method.QualifiedMethodId);
    }
}
