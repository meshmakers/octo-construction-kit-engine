using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#8684 — an Upsert replaces the stored entity with a new document. It keeps the creation stamp of the stored
///     entity (a blueprint update made every seeded pipeline look recreated), and it never falls back to the imported
///     values when the stored entities cannot be read: the import stops before anything is written, instead of
///     replacing the runtime state (a deployed pipeline's DeploymentState) with the seed's.
/// </summary>
public class ImportRtModelCommandUpsertStoredStateTests
{
    private const string Tenant = "tenant1";
    private const string Model = "Test-1.0.0";
    private const string TypeId = "Test/Item";

    private static readonly DateTime StoredCreation = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    private static CkTypeGraph Graph()
    {
        var name = Attr("Name", AttributeOwnershipDto.SeedOwned);
        var state = Attr("State", AttributeOwnershipDto.RuntimeState);
        return new CkTypeGraph(new CkId<CkTypeId>($"{Model}/Item"),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>
            {
                [name.CkAttributeId] = name, [state.CkAttributeId] = state
            },
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private static CkTypeAttributeGraph Attr(string name, AttributeOwnershipDto ownership)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = name, ValueType = AttributeValueTypesDto.String, Ownership = ownership });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = true }, definition);
    }

    private static RtCkId<CkAttributeId> AttrId(string name) =>
        Graph().AllAttributes.Values.First(a => a.AttributeName == name).CkAttributeId.ToRtCkId();

    private static RtEntityTcDto SeedDto(OctoObjectId rtId)
    {
        var dto = new RtEntityTcDto { RtId = rtId, CkTypeId = Rt(TypeId) };
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Name"), Value = "seed" });
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId("State"), Value = "Undeployed" });
        return dto;
    }

    private sealed class Harness
    {
        public Harness(ILogger<ImportRtModelCommand>? logger = null)
        {
            var graph = Graph();
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => CkCache.IsTenantLoaded(Tenant)).Returns(true);
            A.CallTo(() => CkCache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
                .Returns(new List<CkModelIdVersionRange>());
            A.CallTo(() => CkCache.GetRtCkType(Tenant, Rt(TypeId))).Returns(graph);
            CkTypeGraph? outGraph;
            A.CallTo(() => CkCache.TryGetRtCkType(Tenant, Rt(TypeId), out outGraph)).Returns(true)
                .AssignsOutAndRefParameters(graph);
            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                    A<BulkOperationOptions>._))
                .Invokes(call => Written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
            Command = new ImportRtModelCommand(logger ?? NullLogger<ImportRtModelCommand>.Instance, CkCache,
                A.Fake<IRtYamlSerializer>(), A.Fake<IRtJsonSerializer>(), A.Fake<IRtImportAuditTrail>(),
                new RtImportOptions(), A.Fake<IDataPermissionResolver>(), A.Fake<IAuditEventSink>());
        }

        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public ImportRtModelCommand Command { get; }
        public List<RtEntity> Written { get; } = [];

        public void Stored(params RtEntity[] entities)
        {
            A.CallTo(() => Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, Rt(TypeId),
                    A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
                .Returns(Task.FromResult<IResultSet<RtEntity>>(
                    new ResultSet<RtEntity>(entities.ToList(), entities.Length, null, null)));
        }

        public Task UpsertAsync(params RtEntityTcDto[] entities) =>
            Command.ImportModelAsync(Repository, new RtModelRootTcDto { Entities = entities.ToList() },
                ImportStrategy.Upsert);
    }

    [Fact]
    public async Task Upsert_ExistingEntity_KeepsCreationStampAndRuntimeState()
    {
        var h = new Harness();
        var rtId = OctoObjectId.GenerateNewId();
        h.Stored(new RtEntity(Rt(TypeId), rtId, new Dictionary<string, object?> { ["Name"] = "old", ["State"] = "Deployed" })
        {
            RtCreationDateTime = StoredCreation, RtCreatedBy = "installer"
        });

        await h.UpsertAsync(SeedDto(rtId));

        var written = Assert.Single(h.Written);
        Assert.Equal(StoredCreation, written.RtCreationDateTime);
        Assert.Equal("installer", written.RtCreatedBy);
        Assert.Equal("Deployed", written.GetAttributeStringValueOrDefault("State"));
        Assert.Equal("seed", written.GetAttributeStringValueOrDefault("Name"));
    }

    [Fact]
    public async Task Upsert_NewEntity_GetsImportTimeAsCreationStamp()
    {
        var h = new Harness();
        h.Stored();
        var before = DateTime.UtcNow;

        await h.UpsertAsync(SeedDto(OctoObjectId.GenerateNewId()));

        var written = Assert.Single(h.Written);
        Assert.True(written.RtCreationDateTime >= before);
        Assert.Equal("Undeployed", written.GetAttributeStringValueOrDefault("State"));
    }

    [Fact]
    public async Task Upsert_ModelWithCreationStamp_KeepsTheModelValue()
    {
        var h = new Harness();
        var rtId = OctoObjectId.GenerateNewId();
        h.Stored(new RtEntity(Rt(TypeId), rtId) { RtCreationDateTime = StoredCreation });
        var exported = new DateTime(2025, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var dto = SeedDto(rtId);
        dto.RtCreationDateTime = exported;

        await h.UpsertAsync(dto);

        // An export carries its creation stamp, and the import restores it.
        Assert.Equal(exported, Assert.Single(h.Written).RtCreationDateTime);
    }

    [Fact]
    public async Task Upsert_StoredEntitiesNotReadable_StopsBeforeAnyWrite()
    {
        var h = new Harness();
        A.CallTo(() => h.Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, Rt(TypeId),
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ThrowsAsync(new InvalidOperationException("read failed"));

        var act = () => h.UpsertAsync(SeedDto(OctoObjectId.GenerateNewId()));

        var exception = await Assert.ThrowsAsync<ExchangeException>(act);
        Assert.Contains(TypeId, exception.Message);
        Assert.Contains("read failed", exception.Message);
        // The seed values must not replace a runtime state that could not be read.
        Assert.Empty(h.Written);
    }

    [Fact]
    public async Task Upsert_StoredReadCancelled_StaysACancellation()
    {
        var h = new Harness();
        A.CallTo(() => h.Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, Rt(TypeId),
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.UpsertAsync(SeedDto(OctoObjectId.GenerateNewId())));
        Assert.Empty(h.Written);
    }

    [Fact]
    public async Task Upsert_TypeMissingInStaleCacheAfterTenantUpdate_ReloadsOnceAndKeepsDeploymentState()
    {
        // AB#8684 (test-2 reset 24.10.): after a tenant update the loaded cache misses the type on the first lookup.
        var h = new Harness();
        var rtId = OctoObjectId.GenerateNewId();
        h.Stored(new RtEntity(Rt(TypeId), rtId, new Dictionary<string, object?> { ["State"] = "Deployed" }));
        var graph = Graph();
        CkTypeGraph? outGraph;
        A.CallTo(() => h.CkCache.TryGetRtCkType(Tenant, Rt(TypeId), out outGraph)).Returns(false).Once()
            .Then.Returns(true).AssignsOutAndRefParameters(graph);

        await h.UpsertAsync(SeedDto(rtId));

        A.CallTo(() => h.Repository.LoadCacheForTenantAsync(h.CkCache)).MustHaveHappenedOnceExactly();
        // The seed value UNDEPLOYED must not overwrite the stored state.
        Assert.Equal("Deployed", Assert.Single(h.Written).GetAttributeStringValueOrDefault("State"));
    }

    [Fact]
    public async Task Upsert_TypeStillMissingAfterReload_AbortsWithoutWriteAndLogsTenantTypeAndCount()
    {
        var logger = new CapturingLogger();
        var h = new Harness(logger);
        h.Stored(new RtEntity(Rt(TypeId), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["State"] = "Deployed" }));
        CkTypeGraph? outGraph;
        A.CallTo(() => h.CkCache.TryGetRtCkType(Tenant, Rt(TypeId), out outGraph)).Returns(false);

        var act = () => h.UpsertAsync(SeedDto(OctoObjectId.GenerateNewId()), SeedDto(OctoObjectId.GenerateNewId()));

        var exception = await Assert.ThrowsAsync<ExchangeException>(act);
        Assert.Contains(TypeId, exception.Message);
        Assert.Contains(Tenant, exception.Message);
        Assert.Contains("Nothing was written", exception.Message);
        A.CallTo(() => h.Repository.LoadCacheForTenantAsync(h.CkCache)).MustHaveHappenedOnceExactly();
        Assert.Empty(h.Written);
        A.CallTo(() => h.Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
            A<BulkOperationOptions>._)).MustNotHaveHappened();

        var error = Assert.Single(logger.Entries,
            e => e.Level == LogLevel.Error && e.Message.StartsWith("Import stopped before any write"));
        Assert.Contains(TypeId, error.Message);
        Assert.Contains(Tenant, error.Message);
        Assert.Contains("2 imported entity", error.Message);
    }

    private sealed class CapturingLogger : ILogger<ImportRtModelCommand>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
