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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     A user-initiated import (caller path) cannot create a NEW entity of a type whose static method declares
///     <c>replacesGenericCreate</c> (own or inherited): the whole file fails before the first write with one
///     <see cref="ExchangeException"/> that carries message 137, the reason token and, per offender, the rtId, the
///     CK type id and the method id. An entity whose rtId is stored is updated as before. The system path (no caller)
///     is not checked by the import command; the blueprint writers check before they call it.
/// </summary>
public class ImportRtModelCommandGenericCreateTests : IDisposable
{
    private const string Tenant = "tenant1";
    private const string Model = "Test-1.0.0";
    private const string AccountType = "Test/Account";
    private const string SpecialAccountType = "Test/SpecialAccount";
    private const string PlainType = "Test/Plain";
    private const string MethodName = "Create-1";

    private readonly List<string> _tempFiles = [];
    private static readonly RtImportCallerContext User = RtImportCallerContext.UserInitiated("user-1");

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    private string TempFile()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        return path;
    }

    private static readonly string ExpectedMethodId = Method().QualifiedMethodId;

    private static CkMethodGraph Method() => new(new CkId<CkTypeId>($"{Model}/Account"),
        new CkMethodDto { MethodId = MethodName, Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true });

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    private static CkTypeAttributeGraph NameAttr()
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/Name");
        var definition = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = "Name", ValueType = AttributeValueTypesDto.String });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = "Name", IsOptional = true }, definition);
    }

    private static CkTypeGraph Graph(string rtId, bool withMethod)
    {
        var name = NameAttr();
        var method = Method();
        return new CkTypeGraph(new CkId<CkTypeId>(rtId.Replace("Test/", $"{Model}/")),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> { [name.CkAttributeId] = name },
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false,
            allMethods: withMethod ? new Dictionary<string, CkMethodGraph> { [MethodName] = method } : null);
    }

    private const string IdentityUserType = "System.Identity/User";

    private static CkTypeGraph IdentityUserGraph()
    {
        var name = NameAttr();
        var create = new CkMethodGraph(new CkId<CkTypeId>("System.Identity-3.0.0/User"),
            new CkMethodDto { MethodId = "CreateUser-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true });
        return new CkTypeGraph(new CkId<CkTypeId>("System.Identity-3.0.0/User"),
            isAbstract: false, isFinal: true, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> { [name.CkAttributeId] = name },
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false,
            allMethods: new Dictionary<string, CkMethodGraph> { ["CreateUser-1"] = create });
    }

    private sealed class Harness
    {
        public Harness(bool tenantHasFlaggedType = true, bool withIdentityUser = false)
        {
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._))
                .Returns(Task.FromResult(new RtDataPolicyTable([])));
            A.CallTo(() => CkCache.IsTenantLoaded(Tenant)).Returns(true);
            A.CallTo(() => CkCache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
                .Returns(new List<CkModelIdVersionRange>());

            // Account declares the replacing method, SpecialAccount derives (inherits it), Plain has none.
            var graphs = new List<CkTypeGraph>();
            foreach (var (type, flagged) in new[]
                         { (AccountType, true), (SpecialAccountType, true), (PlainType, false) })
            {
                var graph = Graph(type, tenantHasFlaggedType && flagged);
                graphs.Add(graph);
                var graphOut = (CkTypeGraph?)graph;
                A.CallTo(() => CkCache.GetRtCkType(Tenant, Rt(type))).Returns(graph);
                A.CallTo(() => CkCache.TryGetRtCkType(Tenant, Rt(type), out graphOut)).Returns(true)
                    .AssignsOutAndRefParameters(graph);
            }

            if (withIdentityUser)
            {
                // AB#7777 / AB#8053: System.Identity/User with its static CreateUser-1, flagged replacesGenericCreate (System.Identity 3.0.0).
                var user = IdentityUserGraph();
                graphs.Add(user);
                var userOut = (CkTypeGraph?)user;
                A.CallTo(() => CkCache.GetRtCkType(Tenant, Rt(IdentityUserType))).Returns(user);
                A.CallTo(() => CkCache.TryGetRtCkType(Tenant, Rt(IdentityUserType), out userOut)).Returns(true)
                    .AssignsOutAndRefParameters(user);
            }

            A.CallTo(() => CkCache.GetCkTypes(Tenant)).Returns(graphs);

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
        public IRtYamlSerializer Yaml { get; } = A.Fake<IRtYamlSerializer>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = new();
        public List<RtEntity> Written { get; } = [];

        public ImportRtModelCommand CreateCommand()
        {
            return new ImportRtModelCommand(NullLogger<ImportRtModelCommand>.Instance, CkCache, Yaml, Json,
                A.Fake<IRtImportAuditTrail>(), new RtImportOptions(), Resolver, A.Fake<IAuditEventSink>());
        }

        public RtEntity AddStored(string ckType)
        {
            var entity = new RtEntity(Rt(ckType), OctoObjectId.GenerateNewId());
            entity.SetAttributeRawValue("Name", "stored");
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
                .ReturnsLazily(() => Task.FromResult<IRtDeserializeStream>(new FakeRtDeserializeStream(bulks)));
        }

        public void NothingMayBeWritten()
        {
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                A<BulkOperationOptions>._)).MustNotHaveHappened();
            A.CallTo(() => Session.CommitTransactionAsync()).MustNotHaveHappened();
        }
    }

    private static RtEntityTcDto Dto(string ckType, OctoObjectId? rtId = null, string name = "incoming")
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/Name").ToRtCkId();
        var dto = new RtEntityTcDto { RtId = rtId ?? OctoObjectId.GenerateNewId(), CkTypeId = Rt(ckType) };
        dto.Attributes.Add(new RtAttributeTcDto { Id = attrId, Value = name });
        return dto;
    }

    /// <summary>
    ///     AB#8325: the JSON import writes its bulks inside the awaited call, so the entities are written when it returns.
    /// </summary>
    private static void AssertWritten(Harness h, Func<RtEntity, bool> predicate) =>
        Assert.Contains(h.Written.ToArray(), new Predicate<RtEntity>(predicate));

    private static void AssertRefusal(ExchangeException ex, params ValueTuple<string, OctoObjectId>[] offenders)
    {
        Assert.Equal(137, ex.MessageNumber);
        Assert.Contains(GenericCreateReplacement.ReasonToken, ex.Message);
        Assert.Equal(offenders.Length, ex.Messages.Count);
        foreach (var (type, rtId) in offenders)
        {
            var message = Assert.Single(ex.Messages, m => m.Parameters["rtId"] == rtId.ToString());
            Assert.Equal(137, message.MessageNumber);
            Assert.Equal(type, message.Parameters["ckTypeId"]);
            Assert.Equal(ExpectedMethodId, message.Parameters["methodId"]);
            Assert.Equal(GenericCreateReplacement.ReasonToken, message.Parameters["reason"]);
            Assert.Contains(rtId.ToString(), ex.Message);
        }
    }

    [Theory]
    [InlineData(ImportStrategy.Insert)]
    [InlineData(ImportStrategy.Upsert)]
    public async Task NewEntityOfFlaggedType_Yaml_FailsWithOneError_AndWritesNothing(ImportStrategy strategy)
    {
        var h = new Harness();
        var flagged = Dto(AccountType);
        h.YamlReturns(Dto(PlainType), flagged);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, strategy, User));

        AssertRefusal(ex, (AccountType, flagged.RtId));
        Assert.Empty(h.Written);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task NewEntityOfDerivedType_IsRefused_WithTheInheritedMethod()
    {
        var h = new Harness();
        var derived = Dto(SpecialAccountType);
        h.YamlReturns(derived);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert,
                User));

        AssertRefusal(ex, (SpecialAccountType, derived.RtId));
    }

    [Fact]
    public async Task SeveralOffenders_AreListedInOneError_AndTheStoredOneIsNotAnOffender()
    {
        var h = new Harness();
        var stored = h.AddStored(AccountType);
        var new1 = Dto(AccountType);
        var new2 = Dto(SpecialAccountType);
        h.YamlReturns(new1, Dto(AccountType, stored.RtId), new2);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert,
                User));

        AssertRefusal(ex, (AccountType, new1.RtId), (SpecialAccountType, new2.RtId));
        Assert.DoesNotContain(stored.RtId.ToString(), ex.Message);
        Assert.Empty(h.Written);
    }

    [Fact]
    public async Task ManyOffenders_AllAreInMessages_ButTheExceptionTextNamesOnlyTheFirstTwenty()
    {
        var h = new Harness();
        var offenders = Enumerable.Range(0, 25).Select(_ => Dto(AccountType)).ToArray();
        h.YamlReturns(offenders);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Insert,
                User));

        Assert.Equal(25, ex.Messages.Count);
        Assert.Contains(offenders[0].RtId.ToString(), ex.Message);
        Assert.Contains(offenders[19].RtId.ToString(), ex.Message);
        Assert.DoesNotContain(offenders[20].RtId.ToString(), ex.Message);
        Assert.Contains("and 5 more", ex.Message);
    }

    [Fact]
    public async Task ExistingEntityOfFlaggedType_Upsert_IsUpdatedAsBefore()
    {
        var h = new Harness();
        var stored = h.AddStored(AccountType);
        h.YamlReturns(Dto(AccountType, stored.RtId, "changed"), Dto(PlainType));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, User);

        Assert.Equal(2, h.Written.Count);
        Assert.Equal("changed", h.Written.Single(e => e.RtId == stored.RtId).GetAttributeValueOrDefault("Name"));
    }

    [Fact]
    public async Task NewEntityOfFlaggedType_InJsonBulks_IsRefusedBeforeTheFirstBulkIsWritten()
    {
        var h = new Harness();
        var flagged = Dto(AccountType);
        h.JsonReturnsBulks([Dto(PlainType), Dto(PlainType)], [flagged]);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsCallerAsync(h.Repository, TempFile(), "application/json", ImportStrategy.Upsert,
                User));

        AssertRefusal(ex, (AccountType, flagged.RtId));
        Assert.Empty(h.Written);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task ExistingEntityOfFlaggedType_InJsonBulks_Upsert_IsUpdatedAsBefore()
    {
        var h = new Harness();
        var stored = h.AddStored(AccountType);
        h.JsonReturnsBulks([Dto(AccountType, stored.RtId, "changed")], [Dto(PlainType)]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Upsert, User);

        AssertWritten(h, e => e.RtId == stored.RtId);
        Assert.Equal("changed", h.Written.Single(e => e.RtId == stored.RtId).GetAttributeValueOrDefault("Name"));
    }

    [Fact]
    public async Task TenantWithoutFlaggedType_ImportsAsBefore_AndReadsTheJsonFileOnlyOnce()
    {
        var h = new Harness(tenantHasFlaggedType: false);
        h.JsonReturnsBulks([Dto(AccountType)], [Dto(PlainType)]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Insert, User);

        AssertWritten(h, _ => true);
        // no first pass over the file and no read of the stored state
        A.CallTo(() => h.Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => h.Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
            A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
    }

    // ------------------------------------------------------------------ system path pinned

    [Fact]
    public async Task SystemPath_ImportAsync_WithoutCaller_IsNotCheckedByTheCommand()
    {
        // The blueprint writers run the same check (GenericCreateNewEntityCheck) before they call the command; the
        // command itself stays a plain data-transfer on the system path.
        var h = new Harness();
        h.YamlReturns(Dto(AccountType));

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert);

        Assert.Single(h.Written);
    }

    [Fact]
    public async Task SystemPath_ImportModelAsync_IsNotCheckedByTheCommand()
    {
        var h = new Harness();

        await h.CreateCommand().ImportModelAsync(h.Repository,
            new RtModelRootTcDto { Entities = [Dto(AccountType)] }, ImportStrategy.Upsert);

        Assert.Single(h.Written);
    }

    // ---- AB#7777: System.Identity/User is created only through User.CreateUser-1 -------------------

    [Theory]
    [InlineData(ImportStrategy.Insert)]
    [InlineData(ImportStrategy.Upsert)]
    public async Task NewIdentityUser_IsRefused_WithUserCreateUser_AndNothingIsWritten(ImportStrategy strategy)
    {
        var h = new Harness(tenantHasFlaggedType: false, withIdentityUser: true);
        var user = Dto(IdentityUserType);
        h.YamlReturns(Dto(PlainType), user);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, strategy, User));

        Assert.Equal(137, ex.MessageNumber);
        var message = Assert.Single(ex.Messages);
        Assert.Equal(user.RtId.ToString(), message.Parameters["rtId"]);
        Assert.Equal(IdentityUserType, message.Parameters["ckTypeId"]);
        Assert.Equal("System.Identity/User.CreateUser-1", message.Parameters["methodId"]);
        Assert.Equal(GenericCreateReplacement.ReasonToken, message.Parameters["reason"]);
        Assert.Empty(h.Written);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task ExistingIdentityUser_Upsert_IsUpdatedAsBefore()
    {
        var h = new Harness(tenantHasFlaggedType: false, withIdentityUser: true);
        var stored = h.AddStored(IdentityUserType);
        h.YamlReturns(Dto(IdentityUserType, stored.RtId, "changed"));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, User);

        Assert.Equal("changed", Assert.Single(h.Written).GetAttributeValueOrDefault("Name"));
    }

    [Fact]
    public async Task NewIdentityUser_OnTheSystemPath_IsNotCheckedByTheImportCommand()
    {
        // The system import (no caller) is unchanged; blueprint writers run their own guard before they call it.
        var h = new Harness(tenantHasFlaggedType: false, withIdentityUser: true);
        var user = Dto(IdentityUserType);
        h.YamlReturns(user);

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert);

        Assert.Contains(h.Written, e => e.RtId == user.RtId);
    }
}
