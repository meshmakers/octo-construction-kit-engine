using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
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
using Meshmakers.Octo.Runtime.Engine.TransportContainer;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#6392 — blueprint-lock protection of the ImportRt route: a user-initiated import (caller context with
///     <c>EnforceBlueprintLock</c>) into an opted-in type fails atomically before any write when a stored entity
///     is locked (every offender listed), AuditOnly writes and audits, the blueprint stamps of an export are
///     stripped on import, system flows and non-opted-in types are untouched, and tenants without an opted-in
///     policy cost nothing.
/// </summary>
public class ImportRtModelCommandBlueprintLockTests : IDisposable
{
    private const string Tenant = "tenant1";
    private const string RuleType = "Test/CategorizationRule";
    private const string OtherType = "Test/Contact";
    private const string Model = "Test-1.0.0";

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    private static RtDataPolicyTable Table(bool protect = true, bool auditOnly = false, string target = RuleType)
    {
        return new RtDataPolicyTable(
        [
            new RtDataPolicyRule("rules", new HashSet<string> { target },
                [RtDataAction.Read, RtDataAction.Write, RtDataAction.Delete], OwnedOnly: false,
                AuditOnly: auditOnly, new HashSet<string> { "Accountant" }, protect)
        ]);
    }

    private static CkTypeAttributeGraph Attr(string name, AttributeValueTypesDto valueType)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto { AttributeId = name, ValueType = valueType });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = true }, definition);
    }

    private static CkTypeGraph Graph(string rtId)
    {
        var attrs = new[]
        {
            Attr("Name", AttributeValueTypesDto.String),
            Attr("RtBlueprintLocked", AttributeValueTypesDto.Boolean),
            Attr("RtBlueprintSource", AttributeValueTypesDto.String),
            Attr("RtBlueprintAppliedAt", AttributeValueTypesDto.DateTime)
        };
        return new CkTypeGraph(new CkId<CkTypeId>(rtId.Replace("Test/", $"{Model}/")),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a), indexes: [],
            associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private sealed class Harness
    {
        private readonly Func<RtDataPolicyTable> _table;

        public Harness(RtDataPolicyTable table)
        {
            _table = () => table;
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._))
                .ReturnsLazily(() => Task.FromResult(_table()));
            A.CallTo(() => CkCache.IsTenantLoaded(Tenant)).Returns(true);
            A.CallTo(() => CkCache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
                .Returns(new List<CkModelIdVersionRange>());

            foreach (var type in new[] { RuleType, OtherType })
            {
                var graph = Graph(type);
                var graphOut = (CkTypeGraph?)graph;
                A.CallTo(() => CkCache.GetRtCkType(Tenant, Rt(type))).Returns(graph);
                A.CallTo(() => CkCache.TryGetRtCkType(Tenant, Rt(type), out graphOut)).Returns(true)
                    .AssignsOutAndRefParameters(graph);
            }

            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            A.CallTo(() => Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                    A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
                .ReturnsLazily(call =>
                {
                    var ids = call.GetArgument<IReadOnlyList<OctoObjectId>>(2)!;
                    var items = ids.Where(Stored.ContainsKey).Select(id => Stored[id]).ToList();
                    return Task.FromResult<IResultSet<RtEntity>>(new ResultSet<RtEntity>(items, items.Count, null,
                        null));
                });
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                    A<BulkOperationOptions>._))
                .Invokes(call => Written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
        }

        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public IAuditEventSink AuditSink { get; } = A.Fake<IAuditEventSink>();
        public IRtYamlSerializer Yaml { get; } = A.Fake<IRtYamlSerializer>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = new();
        public List<RtEntity> Written { get; } = [];

        public int StoredReads => Fake.GetCalls(Repository)
            .Count(c => c.Method.Name == nameof(IRuntimeRepository.GetRtEntitiesByIdAsync));

        public ImportRtModelCommand CreateCommand()
        {
            return new ImportRtModelCommand(NullLogger<ImportRtModelCommand>.Instance, CkCache, Yaml, Json,
                A.Fake<IRtImportAuditTrail>(), new RtImportOptions(), Resolver, AuditSink);
        }

        public RtEntity AddStored(string ckType, bool? locked, string? source = null, string? name = "stored")
        {
            var entity = new RtEntity(Rt(ckType), OctoObjectId.GenerateNewId());
            entity.SetAttributeRawValue("Name", name);
            if (locked != null)
            {
                entity.SetAttributeRawValue("RtBlueprintLocked", locked);
            }

            if (source != null)
            {
                entity.SetAttributeRawValue("RtBlueprintSource", source);
                entity.SetAttributeRawValue("RtBlueprintAppliedAt", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            }

            Stored[entity.RtId] = entity;
            return entity;
        }

        public void YamlReturns(params RtEntityTcDto[] entities)
        {
            var root = new RtModelRootTcDto { Entities = entities.ToList() };
            A.CallTo(() => Yaml.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
                .Returns(Task.FromResult(root));
        }

        public void JsonReturnsBulks(params RtEntityTcDto[][] bulks)
        {
            A.CallTo(() => Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
                .ReturnsLazily(() => Task.FromResult<IRtDeserializeStream>(new FakeDeserializeStream(bulks)));
        }
    }

    private sealed class FakeDeserializeStream(RtEntityTcDto[][] bulks) : IRtDeserializeStream
    {
        public IReadOnlyCollection<CkModelIdVersionRange> Dependencies { get; } = [];
        public event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

        public Task ReadAsync(CancellationToken? cancellationToken = null)
        {
            foreach (var bulk in bulks)
            {
                BulkDeserialized?.Invoke(this, new RtDeserializeEventArgs(bulk));
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private string TempFile()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        return path;
    }

    private static RtEntityTcDto Dto(string ckType, OctoObjectId? rtId = null, bool? locked = null,
        string? source = null, DateTime? appliedAt = null, string name = "incoming")
    {
        var graph = Graph(ckType);
        RtCkId<CkAttributeId> Id(string n) => graph.AllAttributes.Values.First(a => a.AttributeName == n)
            .CkAttributeId.ToRtCkId();

        var dto = new RtEntityTcDto { RtId = rtId ?? OctoObjectId.GenerateNewId(), CkTypeId = Rt(ckType) };
        dto.Attributes.Add(new RtAttributeTcDto { Id = Id("Name"), Value = name });
        if (locked != null)
        {
            dto.Attributes.Add(new RtAttributeTcDto { Id = Id("RtBlueprintLocked"), Value = locked });
        }

        if (source != null)
        {
            dto.Attributes.Add(new RtAttributeTcDto { Id = Id("RtBlueprintSource"), Value = source });
        }

        if (appliedAt != null)
        {
            dto.Attributes.Add(new RtAttributeTcDto { Id = Id("RtBlueprintAppliedAt"), Value = appliedAt });
        }

        return dto;
    }

    private static readonly RtImportCallerContext User = RtImportCallerContext.UserInitiated("user-1");

    private const string Yamlct = ExchangeMimeTypes.MimeTypeYaml;

    // ------------------------------------------------------------------ preflight

    [Fact]
    public async Task LockedOffender_FailsAtomically_ListsAllOffenders_AndWritesNothing()
    {
        var h = new Harness(Table());
        var locked1 = h.AddStored(RuleType, locked: true, source: "bp");
        var locked2 = h.AddStored(RuleType, locked: true, source: "bp");
        var unlocked = h.AddStored(RuleType, locked: false);
        h.YamlReturns(Dto(RuleType, locked1.RtId), Dto(RuleType, unlocked.RtId), Dto(RuleType, locked2.RtId),
            Dto(RuleType));
        var command = h.CreateCommand();

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => command.ImportAsCallerAsync(h.Repository,
            TempFile(), Yamlct, ImportStrategy.Upsert, User));

        Assert.Equal(6384, ex.MessageNumber);
        Assert.Contains($"{RuleType}@{locked1.RtId}", ex.Message);
        Assert.Contains($"{RuleType}@{locked2.RtId}", ex.Message);
        Assert.DoesNotContain(unlocked.RtId.ToString(), ex.Message);
        Assert.Contains("locked by blueprint", ex.Message);
        A.CallTo(() => h.Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
            A<BulkOperationOptions>._)).MustNotHaveHappened();
        A.CallTo(() => h.Session.CommitTransactionAsync()).MustNotHaveHappened();
        Assert.Equal(1, h.StoredReads);
    }

    [Fact]
    public async Task LockedOffender_InJsonBulks_ListsOffendersOfAllBulks_BeforeTheFirstWrite()
    {
        var h = new Harness(Table());
        var locked1 = h.AddStored(RuleType, locked: true);
        var locked2 = h.AddStored(RuleType, locked: true);
        h.JsonReturnsBulks([Dto(RuleType, locked1.RtId), Dto(RuleType)], [Dto(RuleType, locked2.RtId)]);
        var command = h.CreateCommand();

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => command.ImportAsCallerAsync(h.Repository,
            TempFile(), "application/json", ImportStrategy.Upsert, User));

        Assert.Equal(6384, ex.MessageNumber);
        Assert.Contains(locked1.RtId.ToString(), ex.Message);
        Assert.Contains(locked2.RtId.ToString(), ex.Message);
        A.CallTo(() => h.Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
            A<BulkOperationOptions>._)).MustNotHaveHappened();
        Assert.Equal(1, h.StoredReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnlockedOrAbsentFlag_Passes_AndOnlyOneBatchedReadIsMadePerType(bool flagStored)
    {
        var h = new Harness(Table());
        var existing = h.AddStored(RuleType, locked: flagStored ? false : null);
        h.YamlReturns(Dto(RuleType, existing.RtId, name: "changed"), Dto(RuleType));
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User);

        Assert.Equal(2, h.Written.Count);
        Assert.Equal("changed", h.Written.Single(e => e.RtId == existing.RtId).GetAttributeValueOrDefault("Name"));
        // the preflight read is shared with the preserve pass: ONE read for the opted-in type
        Assert.Equal(1, h.StoredReads);
    }

    [Fact]
    public async Task AuditOnly_WritesTheLockedEntity_PublishesAnAuditEvent_AndKeepsTheStamps()
    {
        var h = new Harness(Table(auditOnly: true));
        var locked = h.AddStored(RuleType, locked: true, source: "bp");
        h.YamlReturns(Dto(RuleType, locked.RtId, name: "overwritten"));
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User);

        var written = Assert.Single(h.Written);
        Assert.Equal("overwritten", written.GetAttributeValueOrDefault("Name"));
        Assert.Equal(true, written.GetAttributeValueOrDefault("RtBlueprintLocked"));
        Assert.Equal("bp", written.GetAttributeValueOrDefault("RtBlueprintSource"));
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>.That.Matches(e =>
            e.Category == "DataPermissions.BlueprintLockViolation"))).MustHaveHappenedOnceExactly();
        Assert.Equal(1, command.BlueprintLockSummary.AuditedViolationCount);
        Assert.Equal(1, h.StoredReads);
    }

    // ------------------------------------------------------------------ round trip / stripping

    [Fact]
    public async Task ExportImportRoundTrip_StripsTheStamps_NewEntitiesBecomeTenantOwned_AndReportsTheCount()
    {
        // Tenant A: two locked, blueprint-stamped entities; exported with the real converter.
        var tenantA = new Harness(Table());
        var exporterA = new RtEntityToTcDtoConverter(tenantA.CkCache);
        var a1 = tenantA.AddStored(RuleType, locked: true, source: "bp-a");
        var a2 = tenantA.AddStored(RuleType, locked: true, source: "bp-a");
        var exported = new[] { a1, a2 }.Select(e => exporterA.Convert(Tenant, e)).ToArray();
        // the export carries the blueprint stamps (name + 3 stamps per entity)
        Assert.All(exported, dto => Assert.Equal(4, dto.Attributes.Count));

        // Tenant B: opted in, empty. User imports the file.
        var tenantB = new Harness(Table());
        tenantB.YamlReturns(exported);
        var command = tenantB.CreateCommand();

        await command.ImportAsCallerAsync(tenantB.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User);

        Assert.Equal(2, tenantB.Written.Count);
        Assert.All(tenantB.Written, e =>
        {
            Assert.Null(e.GetAttributeValueOrDefault("RtBlueprintLocked"));
            Assert.Null(e.GetAttributeValueOrDefault("RtBlueprintSource"));
            Assert.Null(e.GetAttributeValueOrDefault("RtBlueprintAppliedAt"));
        });
        // 2 entities x 3 stamps
        Assert.Equal(new RtImportBlueprintLockSummary(6, 2, 0), command.BlueprintLockSummary);
        A.CallTo(() => tenantB.AuditSink.PublishAsync(A<AuditEvent>.That.Matches(e =>
            e.Category == "RtImport.BlueprintAttributesStripped" && e.Level == AuditEventLevel.Warning)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ExistingUnlockedEntity_KeepsItsStoredBlueprintValues_WhenTheFileCarriesOthers()
    {
        var h = new Harness(Table());
        var applied = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var existing = h.AddStored(RuleType, locked: false, source: "bp-stored");
        h.YamlReturns(Dto(RuleType, existing.RtId, locked: true, source: "forged", appliedAt: DateTime.UtcNow));
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User);

        var written = Assert.Single(h.Written);
        Assert.Equal(false, written.GetAttributeValueOrDefault("RtBlueprintLocked"));
        Assert.Equal("bp-stored", written.GetAttributeValueOrDefault("RtBlueprintSource"));
        Assert.Equal(applied, ((DateTime)written.GetAttributeValueOrDefault("RtBlueprintAppliedAt")!).ToUniversalTime());
        Assert.Equal(3, command.BlueprintLockSummary.StrippedAttributeCount);
    }

    [Fact]
    public async Task InsertStrategy_StripsTheStamps_WithoutReadingTheStoredEntities()
    {
        var h = new Harness(Table());
        h.YamlReturns(Dto(RuleType, locked: true, source: "bp"));
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Insert, User);

        var written = Assert.Single(h.Written);
        Assert.Null(written.GetAttributeValueOrDefault("RtBlueprintLocked"));
        Assert.Null(written.GetAttributeValueOrDefault("RtBlueprintSource"));
        Assert.Equal(0, h.StoredReads);
    }

    [Fact]
    public async Task JsonBulks_StripTheStamps_AndShareOneReadAcrossBulks()
    {
        var h = new Harness(Table());
        var existing = h.AddStored(RuleType, locked: false);
        h.JsonReturnsBulks([Dto(RuleType, locked: true, source: "x")],
            [Dto(RuleType, existing.RtId, locked: true, source: "y")]);
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Upsert, User);

        // The JSON stream hands each bulk to an async-void handler that the command does not await (existing
        // behaviour, not part of AB#6392), so wait for the writes before asserting.
        for (var i = 0; i < 100 && h.Written.Count < 2; i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, h.Written.Count);
        Assert.All(h.Written, e => Assert.NotEqual(true, e.GetAttributeValueOrDefault("RtBlueprintLocked")));
        Assert.All(h.Written, e => Assert.Null(e.GetAttributeValueOrDefault("RtBlueprintSource")));
        Assert.Equal(1, h.StoredReads);
    }

    // ------------------------------------------------------------------ untouched flows

    [Fact]
    public async Task NonOptedInType_IsUntouched_StampsPassThrough()
    {
        // The policy opts in the rule type only; the contact type is not covered.
        var h = new Harness(Table());
        var locked = h.AddStored(OtherType, locked: true, source: "bp");
        h.YamlReturns(Dto(OtherType, locked.RtId, locked: true, source: "bp2"));
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User);

        var written = Assert.Single(h.Written);
        Assert.Equal(true, written.GetAttributeValueOrDefault("RtBlueprintLocked"));
        Assert.Equal("bp2", written.GetAttributeValueOrDefault("RtBlueprintSource"));
        Assert.Equal(RtImportBlueprintLockSummary.Empty, command.BlueprintLockSummary);
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(false)] // policy table has rules but none opted in
    [InlineData(true)] // no rules at all
    public async Task NoOptedInPolicy_IsZeroExtraCost_SameReadsAsAPlainImport(bool emptyTable)
    {
        var table = emptyTable ? RtDataPolicyTable.Empty : Table(protect: false);
        var plain = new Harness(table);
        var plainEntity = plain.AddStored(RuleType, locked: true, source: "bp");
        plain.YamlReturns(Dto(RuleType, plainEntity.RtId, locked: true, source: "bp"));
        await plain.CreateCommand().ImportAsync(plain.Repository, TempFile(), Yamlct, ImportStrategy.Upsert);

        var asCaller = new Harness(table);
        var callerEntity = asCaller.AddStored(RuleType, locked: true, source: "bp");
        asCaller.YamlReturns(Dto(RuleType, callerEntity.RtId, locked: true, source: "bp"));
        var command = asCaller.CreateCommand();
        await command.ImportAsCallerAsync(asCaller.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User);

        Assert.Equal(plain.StoredReads, asCaller.StoredReads);
        Assert.Equal(true, Assert.Single(asCaller.Written).GetAttributeValueOrDefault("RtBlueprintLocked"));
        Assert.Equal(RtImportBlueprintLockSummary.Empty, command.BlueprintLockSummary);
    }

    [Fact]
    public async Task SystemFlow_WithoutCallerContext_IsUnaffected_AndNeverTouchesThePolicyTable()
    {
        var h = new Harness(Table());
        var locked = h.AddStored(RuleType, locked: true, source: "bp");
        h.YamlReturns(Dto(RuleType, locked.RtId, locked: true, source: "bp-new", name: "reapplied"));
        var command = h.CreateCommand();

        await command.ImportAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert);

        var written = Assert.Single(h.Written);
        Assert.Equal("reapplied", written.GetAttributeValueOrDefault("Name"));
        Assert.Equal("bp-new", written.GetAttributeValueOrDefault("RtBlueprintSource"));
        A.CallTo(() => h.Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CallerContextWithoutTheFlag_BehavesLikeASystemFlow()
    {
        var h = new Harness(Table());
        var locked = h.AddStored(RuleType, locked: true);
        h.YamlReturns(Dto(RuleType, locked.RtId, name: "reapplied"));
        var command = h.CreateCommand();

        await command.ImportAsCallerAsync(h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert,
            new RtImportCallerContext { SubjectId = "svc", EnforceBlueprintLock = false });

        Assert.Equal("reapplied", Assert.Single(h.Written).GetAttributeValueOrDefault("Name"));
    }

    [Fact]
    public async Task MissingSubject_IsStillEnforced_NeverTreatedAsSystem()
    {
        var h = new Harness(Table());
        var locked = h.AddStored(RuleType, locked: true);
        h.YamlReturns(Dto(RuleType, locked.RtId));
        var command = h.CreateCommand();

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => command.ImportAsCallerAsync(h.Repository,
            TempFile(), Yamlct, ImportStrategy.Upsert, RtImportCallerContext.UserInitiated(null)));

        Assert.Equal(6384, ex.MessageNumber);
    }

    [Fact]
    public async Task DerivedType_InheritsTheOptIn_OfItsBaseType()
    {
        const string derived = "Test/SpecialRule";
        var h = new Harness(Table());
        var derivedGraph = new CkTypeGraph(new CkId<CkTypeId>($"{Model}/SpecialRule"),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: new CkId<CkTypeId>($"{Model}/CategorizationRule"),
            definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: Graph(RuleType).AllAttributes.ToDictionary(kv => kv.Key, kv => kv.Value), indexes: [],
            associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
        var derivedOut = (CkTypeGraph?)derivedGraph;
        A.CallTo(() => h.CkCache.GetRtCkType(Tenant, Rt(derived))).Returns(derivedGraph);
        A.CallTo(() => h.CkCache.TryGetRtCkType(Tenant, Rt(derived), out derivedOut)).Returns(true)
            .AssignsOutAndRefParameters(derivedGraph);
        var baseOut = Graph(RuleType);
        var baseOutNullable = (CkTypeGraph?)baseOut;
        A.CallTo(() => h.CkCache.TryGetCkType(Tenant, A<CkId<CkTypeId>>._, out baseOutNullable)).Returns(true)
            .AssignsOutAndRefParameters(baseOut);
        var locked = h.AddStored(derived, locked: true);
        h.YamlReturns(Dto(RuleType, locked.RtId));
        // the file names the derived type by id
        var dto = Dto(derived, locked.RtId);
        h.YamlReturns(dto);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(
            h.Repository, TempFile(), Yamlct, ImportStrategy.Upsert, User));

        Assert.Contains(locked.RtId.ToString(), ex.Message);
    }
}
