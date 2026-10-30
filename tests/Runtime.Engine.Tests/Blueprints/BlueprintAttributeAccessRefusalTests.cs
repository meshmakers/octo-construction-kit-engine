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
using Meshmakers.Octo.Runtime.Engine.Tests.Access;
using static Meshmakers.Octo.Runtime.Engine.Tests.Access.AccessTestModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
///     AB#7767 (H3) — a blueprint (seed data of an install, the diff of an update, the Add, Update and Transform steps
///     of a migration) never runs a CK method, so it cannot set an attribute that only a method may write
///     (<c>access: Hidden</c> or <c>MethodOnly</c>): it is refused before anything is written, with message 139,
///     and the blueprint is not recorded. ReadOnly stays writable. Self-made type graph, no System version.
/// </summary>
public class BlueprintAttributeAccessRefusalTests
{
    private static readonly BlueprintId V1 = new("Shop-1.0.0");
    private static readonly BlueprintId V2 = new("Shop-2.0.0");

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

    public BlueprintAttributeAccessRefusalTests()
    {
        Cache(_ckCache);

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
        A.CallTo(() => _repository.TenantId).Returns(Tenant);
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(Tenant, A<CancellationToken>._))
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
        A.CallTo(() => _installations.GetByBlueprintNameAsync(Tenant, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
    }

    private static Task<IResultSet<RtEntity>> Result(IEnumerable<RtEntity> items)
    {
        var set = A.Fake<IResultSet<RtEntity>>();
        A.CallTo(() => set.Items).Returns(items.ToList());
        return Task.FromResult(set);
    }

    private BlueprintService CreateService() => new(
        _ckCache, _catalog, _history, A.Fake<IBlueprintMigrationExecutor>(), A.Fake<IBlueprintMigrationParser>(),
        A.Fake<ICkModelUpgradeService>(), _repositoryProvider, _import, _serializer, A.Fake<IBlueprintNotifications>(),
        _resolver, _installations, _variables, NullLogger<BlueprintService>.Instance);

    private BlueprintMigrationExecutor CreateExecutor() => new(_repositoryProvider, _import, _ckCache,
        NullLogger<BlueprintMigrationExecutor>.Instance);

    private static BlueprintMetaRootDto Meta(BlueprintId id) => new() { BlueprintId = id, SeedDataPath = "seed.yaml" };

    private static string Id(int n) => $"aa{n:x22}";

    private static RtEntityTcDto Seed(RtCkId<CkTypeId> type, string rtId, params (string Name, object? Value)[] attrs) =>
        Dto(type, new OctoObjectId(rtId), attrs);

    private void GivenSeed(BlueprintId id, params RtEntityTcDto[] entities) =>
        _seeds[id.FullName] = new RtModelRootTcDto { Entities = entities.ToList() };

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
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(Tenant, id.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<TenantBlueprintInfo?>(new TenantBlueprintInfo
            {
                BlueprintId = id,
                AppliedAt = DateTime.UtcNow
            }));
        A.CallTo(() => _installations.GetByBlueprintNameAsync(Tenant, id.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(new BlueprintInstallation
            {
                BlueprintId = id,
                InstalledAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                IsDependency = false
            }));
    }

    private RtEntity GivenTenantEntity(RtCkId<CkTypeId> type, string rtId, BlueprintId source, string name = "old")
    {
        var entity = new RtEntity(type, new OctoObjectId(rtId), new Dictionary<string, object?>
        {
            ["RtBlueprintSource"] = source.FullName,
            ["RtBlueprintLocked"] = true,
            ["Name"] = name
        })
        {
            RtWellKnownName = "acc"
        };
        _tenant.Add(entity);
        return entity;
    }

    // ---- install ----------------------------------------------------------------------------

    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("Email")]
    public async Task Install_SeedWithHiddenOrMethodOnlyValue_IsRefused_AndNothingIsWrittenOrRecorded(string attribute)
    {
        var bad = Seed(AccountType, Id(1), ("Name", "n"), (attribute, "x"));
        GivenSeed(V1, Seed(PlainType, Id(3), ("Name", "p")), bad);
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(Tenant, V1, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(_imported);
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._, A<ImportStrategy>._,
            A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
            A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._)).MustNotHaveHappened();
        A.CallTo(() => _installations.UpsertAsync(A<string>._, A<BlueprintInstallation>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _history.AddEntryAsync(A<string>._, A<TenantBlueprintInfo>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        var refusal = Assert.Single(result.OperationResult.Messages, m => m.MessageNumber == 139);
        Assert.Equal(MessageLevel.Error, refusal.MessageLevel);
        Assert.Equal("Test-1.0.0/Account", refusal.Parameters["ckTypeId"]);
        Assert.Equal(attribute, refusal.Parameters["attributePath"]);
        Assert.Equal(Id(1), refusal.Parameters["rtId"]);
    }

    [Fact]
    public async Task Install_SeedWithRecordMemberThatIsHidden_IsRefused()
    {
        GivenSeed(V1, Seed(AccountType, Id(1), ("Profile", RecordDto(("Label", "l"), ("Token", "t")))));
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(Tenant, V1, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(_imported);
        Assert.Equal("Profile.Token",
            Assert.Single(result.OperationResult.Messages, m => m.MessageNumber == 139).Parameters["attributePath"]);
    }

    [Fact]
    public async Task Install_SeedWithoutRestrictedValues_InstallsAsBefore()
    {
        GivenSeed(V1, Seed(AccountType, Id(1), ("Name", "n"), ("Created", "c")), Seed(PlainType, Id(3), ("Name", "p")));
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(Tenant, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.OperationResult.Messages.Select(m => m.MessageText)));
        Assert.Equal(2, _imported.Count);
    }

    // ---- update -----------------------------------------------------------------------------

    [Theory]
    [InlineData(BlueprintUpdateMode.Safe)]
    [InlineData(BlueprintUpdateMode.Merge)]
    [InlineData(BlueprintUpdateMode.Full)]
    public async Task Update_NewEntityWithRestrictedValue_IsRefusedAndNothingIsImported(BlueprintUpdateMode mode)
    {
        GivenInstalled(V1);
        GivenSeed(V1, Seed(PlainType, Id(3), ("Name", "p")));
        GivenSeed(V2, Seed(PlainType, Id(3), ("Name", "p")), Seed(AccountType, Id(1), ("Email", "e")));
        GivenTenantEntity(PlainType, Id(3), V1);

        var result = await CreateService().ApplyUpdateAsync(Tenant, V2, mode,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_imported);
        var error = Assert.Single(result.Errors, e => e.StartsWith("OCTO-CK139:", StringComparison.Ordinal));
        Assert.Contains(Id(1), error);
        Assert.Contains("'Email'", error);
    }

    [Fact]
    public async Task Update_ContinueOnError_DoesNotSoftenTheRefusal()
    {
        GivenInstalled(V1);
        GivenSeed(V1, Seed(PlainType, Id(3), ("Name", "p")));
        GivenSeed(V2, Seed(PlainType, Id(3), ("Name", "changed")), Seed(AccountType, Id(1), ("PasswordHash", "h")));
        GivenTenantEntity(PlainType, Id(3), V1);

        var result = await CreateService().ApplyUpdateAsync(Tenant, V2, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { ContinueOnError = true }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_imported);
    }

    [Fact]
    public async Task Update_WithoutRestrictedValues_AddsAsBefore()
    {
        GivenInstalled(V1);
        GivenSeed(V1, Seed(PlainType, Id(3), ("Name", "p")));
        GivenSeed(V2, Seed(PlainType, Id(3), ("Name", "p")), Seed(AccountType, Id(1), ("Name", "n"), ("Created", "c")));
        GivenTenantEntity(PlainType, Id(3), V1);

        var result = await CreateService().ApplyUpdateAsync(Tenant, V2, BlueprintUpdateMode.Safe,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(_imported, e => e.RtId == new OctoObjectId(Id(1)));
    }

    // ---- migration steps --------------------------------------------------------------------

    private static BlueprintMigrationDto Migration(MigrationStepDto step) => new()
    {
        SourceVersion = V1.FullName,
        TargetVersion = V2.FullName,
        Steps = [step]
    };

    private static MigrationStepDto Step(MigrationActionType action, RtCkId<CkTypeId> type,
        Dictionary<string, object>? data = null, TransformConfigDto? transform = null) => new()
    {
        StepId = "step-1",
        Action = action,
        Target = new EntityTargetDto { CkTypeId = type.SemanticVersionedFullName },
        Data = data == null ? null : JsonSerializer.SerializeToElement(data),
        Transform = transform
    };

    private async Task AssertStepRefused(MigrationStepDto step, string attribute)
    {
        var result = await CreateExecutor().ExecuteAsync(Tenant, Migration(step),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_imported);
        var error = Assert.Single(result.Errors);
        Assert.Contains("OCTO-CK139:", error);
        Assert.Contains($"'{attribute}'", error);
        A.CallTo(() => _repository.ReplaceOneRtEntityByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
            A<OctoObjectId>._, A<RtEntity>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("Email")]
    public async Task MigrationAdd_RestrictedValue_FailsTheStepAndWritesNothing(string attribute)
    {
        var step = Step(MigrationActionType.Add, AccountType, new Dictionary<string, object>
        {
            ["rtId"] = Id(1),
            ["ckTypeId"] = AccountType.SemanticVersionedFullName,
            ["attributes"] = new[] { new Dictionary<string, object> { ["id"] = AttrId(attribute).ToString(), ["value"] = "x" } }
        });

        await AssertStepRefused(step, attribute);
    }

    [Fact]
    public async Task MigrationAdd_WithoutRestrictedValues_AddsAsBefore()
    {
        var step = Step(MigrationActionType.Add, AccountType, new Dictionary<string, object>
        {
            ["rtId"] = Id(1),
            ["ckTypeId"] = AccountType.SemanticVersionedFullName,
            ["attributes"] = new[] { new Dictionary<string, object> { ["id"] = AttrId("Name").ToString(), ["value"] = "n" } }
        });

        var result = await CreateExecutor().ExecuteAsync(Tenant, Migration(step),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(_imported);
    }

    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("Email")]
    public async Task MigrationUpdate_RestrictedAttribute_FailsTheStep_BeforeAnyEntityIsReplaced(string attribute)
    {
        GivenTenantEntity(AccountType, Id(1), V1);
        GivenTenantEntity(AccountType, Id(2), V1);

        await AssertStepRefused(
            Step(MigrationActionType.Update, AccountType, new Dictionary<string, object> { ["Name"] = "n", [attribute] = "x" }),
            attribute);
    }

    [Fact]
    public async Task MigrationUpdate_ReadOnlyAndReadWrite_StillUpdates()
    {
        GivenTenantEntity(AccountType, Id(1), V1);

        var result = await CreateExecutor().ExecuteAsync(Tenant,
            Migration(Step(MigrationActionType.Update, AccountType,
                new Dictionary<string, object> { ["Name"] = "n", ["Created"] = "c" })),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        A.CallTo(() => _repository.ReplaceOneRtEntityByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
            A<OctoObjectId>._, A<RtEntity>._)).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(TransformType.SetValue, null, "Email", "Email")]
    [InlineData(TransformType.Copy, "Name", "PasswordHash", "PasswordHash")]
    [InlineData(TransformType.Rename, "Email", "Name", "Email")]
    [InlineData(TransformType.Delete, "PasswordHash", null, "PasswordHash")]
    public async Task MigrationTransform_RestrictedSourceOrTarget_FailsTheStep(TransformType type, string? source,
        string? target, string restricted)
    {
        GivenTenantEntity(AccountType, Id(1), V1);

        await AssertStepRefused(Step(MigrationActionType.Transform, AccountType, transform: new TransformConfigDto
        {
            Type = type,
            SourceAttribute = source,
            TargetAttribute = target,
            Value = "x"
        }), restricted);
    }
}
