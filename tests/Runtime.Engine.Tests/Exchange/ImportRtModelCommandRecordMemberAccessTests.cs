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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#8141 - the AB#8059 omission rule at record-member level: a caller upsert that omits a record member that only a CK
///     method may write (<c>MethodOnly</c> / <c>Hidden</c>) keeps the stored value (single record by position, record array by
///     record key, nested records), the system path does not, and a record array without a usable record key refuses the write
///     (message 141) instead of losing a stored value. Synthetic model: record <c>Item</c> (key <c>Key</c>; <c>Label</c>,
///     <c>Token</c> MethodOnly, nested <c>Detail</c> with <c>Pin</c> Hidden and <c>Note</c>), record <c>Loose</c> without a key
///     (<c>Label</c>, <c>Token</c> MethodOnly), record <c>HiddenKeyed</c> whose key is MethodOnly.
/// </summary>
public class ImportRtModelCommandRecordMemberAccessTests : IDisposable
{
    private const string Tenant = "tenant-rm";
    private const string ModelId = "Test-1.0.0";
    private static readonly RtCkId<CkTypeId> HolderType = new($"{ModelId}/Holder");
    private static readonly RtImportCallerContext Caller = RtImportCallerContext.UserInitiated("user-1");

    private static readonly CkRecordGraph Detail = Record("Detail", null,
        Attr("Note", null), Attr("Pin", CkAttributeAccessDto.Hidden));

    private static readonly CkRecordGraph Item = Record("Item", "Key",
        Attr("Key", null), Attr("Label", null), Attr("Token", CkAttributeAccessDto.MethodOnly),
        Attr("Detail", null, AttributeValueTypesDto.Record, Detail.CkRecordId),
        Attr("Details", null, AttributeValueTypesDto.RecordArray, Detail.CkRecordId));

    private static readonly CkRecordGraph Loose = Record("Loose", null,
        Attr("Label", null), Attr("Token", CkAttributeAccessDto.MethodOnly));

    private static readonly CkRecordGraph HiddenKeyed = Record("HiddenKeyed", "Key",
        Attr("Key", CkAttributeAccessDto.MethodOnly), Attr("Label", null), Attr("Token", CkAttributeAccessDto.MethodOnly));

    private static readonly CkTypeGraph Holder = Type("Holder",
        Attr("Name", null),
        Attr("Single", null, AttributeValueTypesDto.Record, Item.CkRecordId),
        Attr("Items", null, AttributeValueTypesDto.RecordArray, Item.CkRecordId),
        Attr("Loose", null, AttributeValueTypesDto.RecordArray, Loose.CkRecordId),
        Attr("Keyed", null, AttributeValueTypesDto.RecordArray, HiddenKeyed.CkRecordId));

    private static readonly CkTypeAttributeGraph[] AllRecordAttributes =
        Holder.AllAttributes.Values.Where(a => a.AttributeName != "Name").ToArray();

    private readonly List<string> _tempFiles = [];

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

    private static CkRecordGraph? Resolve(RtCkId<CkRecordId> id) =>
        new[] { Detail, Item, Loose, HiddenKeyed }.FirstOrDefault(r => r.CkRecordId.ToRtCkId().FullName == id.FullName);

    private static Func<object?, object?> Convert(ICkCacheService cache) =>
        v => ImportRtModelCommand.ToTransportValue(cache, Tenant, v);

    // ---- pure walk ---------------------------------------------------------------------------------------------------

    [Fact]
    public void RecordArray_OmittedMethodOnlyMember_IsCarriedByRecordKey_AndNewElementsKeepNothing()
    {
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Items", new List<RtRecord>
        {
            StoredItem("a", "tok-a"), StoredItem("b", "tok-b")
        });
        var seed = Seed(existing, ("Items", new List<object> { ItemDto("b", "B2"), ItemDto("a", "A2"), ItemDto("c", "C") }));

        var count = ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing, AllRecordAttributes, Resolve,
            Convert(Cache()));

        Assert.Equal(2, count);
        var elements = ((List<object>)seed.Attributes.Single().Value!).Cast<RtRecordTcDto>().ToList();
        Assert.Equal("tok-b", Member(elements[0], "Token"));
        Assert.Equal("B2", Member(elements[0], "Label"));
        Assert.Equal("tok-a", Member(elements[1], "Token"));
        Assert.Null(Member(elements[2], "Token")); // new element: nothing stored
    }

    [Fact]
    public void RecordArray_ReorderedElements_KeepTheirOwnValue()
    {
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Items", new List<RtRecord> { StoredItem("a", "tok-a"), StoredItem("b", "tok-b") });
        var seed = Seed(existing, ("Items", new List<object> { ItemDto("b", "x") }));

        ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing, AllRecordAttributes, Resolve, Convert(Cache()));

        var only = ((List<object>)seed.Attributes.Single().Value!).Cast<RtRecordTcDto>().Single();
        Assert.Equal("tok-b", Member(only, "Token"));
    }

    [Fact]
    public void SingleRecord_OmittedMethodOnlyMember_IsCarried_AndNestedRecordsToo()
    {
        var stored = StoredItem("k", "tok", detailPin: "1234");
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Single", stored);
        var item = ItemDto("k", "L");
        var detail = new RtRecordTcDto { CkRecordId = Detail.CkRecordId.ToRtCkId() };
        detail.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Note"), Value = "n" });
        item.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Detail"), Value = detail });
        var seed = Seed(existing, ("Single", item));

        var count = ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing, AllRecordAttributes, Resolve,
            Convert(Cache()));

        Assert.Equal(2, count);
        var written = Assert.IsType<RtRecordTcDto>(seed.Attributes.Single().Value);
        Assert.Equal("tok", Member(written, "Token"));
        Assert.Equal("1234", Member(Assert.IsType<RtRecordTcDto>(MemberValue(written, "Detail")), "Pin"));
        Assert.Equal("n", Member(Assert.IsType<RtRecordTcDto>(MemberValue(written, "Detail")), "Note"));
    }

    [Fact]
    public void NestedRecordArray_IsMatchedByPosition_OfItsOwnParent_OrRefusedWhenKeyless()
    {
        // Details has no record key: the stored element holds a Hidden Pin, the seed writes an element -> refused.
        var storedItem = StoredItem("k", "tok");
        storedItem.SetAttributeRawValue("Details", new List<RtRecord> { StoredDetail("1234") });
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Single", storedItem);
        var item = ItemDto("k", "L");
        var detail = new RtRecordTcDto { CkRecordId = Detail.CkRecordId.ToRtCkId() };
        detail.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Note"), Value = "n" });
        item.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Details"), Value = new List<object> { detail } });
        var seed = Seed(existing, ("Single", item));

        var ex = Assert.Throws<ExchangeException>(() => ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing,
            AllRecordAttributes, Resolve, Convert(Cache())));

        Assert.Equal(141, ex.MessageNumber);
        Assert.Contains("Single.Details[0].Pin", ex.Message);
    }

    [Fact]
    public void KeylessRecordArray_WithStoredProtectedValue_IsRefused_WithMessage141()
    {
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Loose", new List<RtRecord> { StoredLoose("l", "secret") });
        var seed = Seed(existing, ("Loose", new List<object> { LooseDto("l2") }));

        var ex = Assert.Throws<ExchangeException>(() => ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing,
            AllRecordAttributes, Resolve, Convert(Cache())));

        Assert.Equal(141, ex.MessageNumber);
        Assert.Contains("Loose[0].Token", ex.Message);
        Assert.Contains(existing.RtId.ToString(), ex.Messages.Single().MessageText);
    }

    [Fact]
    public void KeylessRecordArray_WithNothingStoredToKeep_IsAllowed()
    {
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Loose", new List<RtRecord> { StoredLoose("l", null) });
        var seed = Seed(existing, ("Loose", new List<object> { LooseDto("l2") }));

        var count = ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing, AllRecordAttributes, Resolve,
            Convert(Cache()));

        Assert.Equal(0, count);
    }

    [Fact]
    public void RecordArray_WhoseKeyIsMethodOnly_CannotBeMatched_AndIsRefused()
    {
        var stored = new RtRecord { CkRecordId = HiddenKeyed.CkRecordId.ToRtCkId() };
        stored.SetAttributeRawValue("Key", "k");
        stored.SetAttributeRawValue("Token", "t");
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Keyed", new List<RtRecord> { stored });
        var element = new RtRecordTcDto { CkRecordId = HiddenKeyed.CkRecordId.ToRtCkId() };
        element.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Label"), Value = "x" });
        var seed = Seed(existing, ("Keyed", new List<object> { element }));

        var ex = Assert.Throws<ExchangeException>(() => ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing,
            AllRecordAttributes, Resolve, Convert(Cache())));

        Assert.Equal(141, ex.MessageNumber);
    }

    [Fact]
    public void WithoutAConverter_TheSystemPath_CarriesNothing_AndRefusesNothing()
    {
        var existing = StoredHolder();
        existing.SetAttributeRawValue("Items", new List<RtRecord> { StoredItem("a", "tok-a") });
        existing.SetAttributeRawValue("Loose", new List<RtRecord> { StoredLoose("l", "secret") });
        var seed = Seed(existing, ("Items", new List<object> { ItemDto("a", "A2") }),
            ("Loose", new List<object> { LooseDto("l2") }));

        var count = ImportRtModelCommand.PreserveSecretRecordMembers(seed, existing, AllRecordAttributes, Resolve);

        Assert.Equal(0, count);
        var element = ((List<object>)seed.Attributes[0].Value!).Cast<RtRecordTcDto>().Single();
        Assert.Null(Member(element, "Token"));
    }

    [Fact]
    public void RecordHasSecretMembers_SeesMethodOnlyMembers_OnlyWhenAsked()
    {
        Assert.False(ImportRtModelCommand.RecordHasSecretMembers(Item, Resolve, []));
        Assert.True(ImportRtModelCommand.RecordHasSecretMembers(Item, Resolve, [], includeMethodOnlyMembers: true));
        Assert.True(ImportRtModelCommand.RecordHasSecretMembers(Detail, Resolve, [], includeMethodOnlyMembers: true));
    }

    // ---- through the import command ---------------------------------------------------------------------------------

    [Fact]
    public async Task CallerUpsert_OmittingARecordMethodOnlyMember_KeepsTheStoredValue_UnderBlankingPolicyAllow()
    {
        var h = new Harness();
        var stored = h.Store(("Items", new List<RtRecord> { StoredItem("a", "tok-a", detailPin: "9") }));
        h.YamlReturns(HolderDto(stored.RtId, ("Name", "n"), ("Items", new List<object> { ItemDto("a", "A2") })));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, Caller, blankingPolicy: RtImportBlankingPolicy.Allow);

        var written = Assert.Single(h.Written);
        var element = Assert.Single((IEnumerable<RtRecord>)written.Attributes["Items"]!);
        Assert.Equal("A2", element.Attributes["Label"]);
        Assert.Equal("tok-a", element.Attributes["Token"]);
    }

    [Fact]
    public async Task SystemUpsert_DoesNotCarryTheRecordMember_AsBefore()
    {
        var h = new Harness();
        var stored = h.Store(("Items", new List<RtRecord> { StoredItem("a", "tok-a") }));
        h.YamlReturns(HolderDto(stored.RtId, ("Name", "n"), ("Items", new List<object> { ItemDto("a", "A2") })));

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, blankingPolicy: RtImportBlankingPolicy.Allow);

        var element = Assert.Single((IEnumerable<RtRecord>)Assert.Single(h.Written).Attributes["Items"]!);
        Assert.False(element.Attributes.TryGetValue("Token", out var token) && token != null);
    }

    [Fact]
    public async Task CallerUpsert_KeylessRecordArrayWithStoredValue_IsRefused_AndWritesNothing()
    {
        var h = new Harness();
        var stored = h.Store(("Loose", new List<RtRecord> { StoredLoose("l", "secret") }));
        h.YamlReturns(HolderDto(stored.RtId, ("Name", "n"), ("Loose", new List<object> { LooseDto("l2") })));

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(
            h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, Caller));

        Assert.Equal(141, ex.MessageNumber);
        Assert.Empty(h.Written);
        A.CallTo(() => h.Session.CommitTransactionAsync()).MustNotHaveHappened();
    }

    [Fact]
    public async Task CallerUpsert_ExplicitValueForTheRecordMember_IsStillRefusedBy139()
    {
        var h = new Harness();
        var stored = h.Store(("Items", new List<RtRecord> { StoredItem("a", "tok-a") }));
        var dto = ItemDto("a", "A2");
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Token"), Value = "planted" });
        h.YamlReturns(HolderDto(stored.RtId, ("Items", new List<object> { dto })));

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(
            h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, Caller));

        Assert.Equal(139, ex.MessageNumber);
        Assert.Empty(h.Written);
    }

    // ---- model helpers ----------------------------------------------------------------------------------------------

    private static RtCkId<CkAttributeId> AttrId(string name) => new CkId<CkAttributeId>($"{ModelId}/{name}").ToRtCkId();

    private static CkTypeAttributeGraph Attr(string name, CkAttributeAccessDto? access,
        AttributeValueTypesDto valueType = AttributeValueTypesDto.String, CkId<CkRecordId>? record = null)
    {
        var attrId = new CkId<CkAttributeId>($"{ModelId}/{name}");
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = true, Access = access },
            new CkAttributeGraph(attrId,
                new CkAttributeDto { AttributeId = name, ValueType = valueType, ValueCkRecordId = record }));
    }

    private static CkRecordGraph Record(string name, string? key, params CkTypeAttributeGraph[] attrs) =>
        new(new CkId<CkRecordId>($"{ModelId}/{name}"), false, false, [], null, [], [],
            attrs.ToDictionary(a => a.CkAttributeId, a => a), name) { RecordKey = key };

    private static CkTypeGraph Type(string name, params CkTypeAttributeGraph[] attrs) => new(
        new CkId<CkTypeId>($"{ModelId}/{name}"), false, false, true, [], null, null, [], [],
        attrs.ToDictionary(a => a.CkAttributeId, a => a), [], new CkGraphDirectedAssociations([]), name, false);

    private static RtEntity StoredHolder() => new(HolderType, OctoObjectId.GenerateNewId());

    private static RtRecord StoredItem(string key, string? token, string? detailPin = null)
    {
        var record = new RtRecord { CkRecordId = Item.CkRecordId.ToRtCkId() };
        record.SetAttributeRawValue("Key", key);
        record.SetAttributeRawValue("Label", "stored");
        record.SetAttributeRawValue("Token", token);
        if (detailPin != null)
        {
            record.SetAttributeRawValue("Detail", StoredDetail(detailPin));
        }

        return record;
    }

    private static RtRecord StoredDetail(string pin)
    {
        var detail = new RtRecord { CkRecordId = Detail.CkRecordId.ToRtCkId() };
        detail.SetAttributeRawValue("Note", "stored-note");
        detail.SetAttributeRawValue("Pin", pin);
        return detail;
    }

    private static RtRecord StoredLoose(string label, string? token)
    {
        var record = new RtRecord { CkRecordId = Loose.CkRecordId.ToRtCkId() };
        record.SetAttributeRawValue("Label", label);
        record.SetAttributeRawValue("Token", token);
        return record;
    }

    private static RtRecordTcDto ItemDto(string key, string label)
    {
        var dto = new RtRecordTcDto { CkRecordId = Item.CkRecordId.ToRtCkId() };
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Key"), Value = key });
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Label"), Value = label });
        return dto;
    }

    private static RtRecordTcDto LooseDto(string label)
    {
        var dto = new RtRecordTcDto { CkRecordId = Loose.CkRecordId.ToRtCkId() };
        dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId("Label"), Value = label });
        return dto;
    }

    private static RtEntityTcDto Seed(RtEntity existing, params (string Name, object? Value)[] attributes) =>
        HolderDto(existing.RtId, attributes);

    private static RtEntityTcDto HolderDto(OctoObjectId rtId, params (string Name, object? Value)[] attributes)
    {
        var dto = new RtEntityTcDto { RtId = rtId, CkTypeId = HolderType };
        foreach (var (name, value) in attributes)
        {
            dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId(name), Value = value });
        }

        return dto;
    }

    private static object? MemberValue(RtRecordTcDto record, string name) =>
        record.Attributes.FirstOrDefault(a => a.Id.Equals(AttrId(name)))?.Value;

    private static object? Member(RtRecordTcDto record, string name) => MemberValue(record, name);

    private static ICkCacheService Cache()
    {
        var cache = A.Fake<ICkCacheService>();
        A.CallTo(() => cache.IsTenantLoaded(Tenant)).Returns(true);
        var holder = (CkTypeGraph?)Holder;
        A.CallTo(() => cache.GetRtCkType(Tenant, HolderType)).Returns(Holder);
        A.CallTo(() => cache.TryGetRtCkType(Tenant, HolderType, out holder)).Returns(true)
            .AssignsOutAndRefParameters(Holder);
        A.CallTo(() => cache.GetCkTypes(Tenant)).Returns(new List<CkTypeGraph> { Holder });
        A.CallTo(() => cache.GetCkRecords(Tenant)).Returns(new List<CkRecordGraph> { Detail, Item, Loose, HiddenKeyed });
        foreach (var record in new[] { Detail, Item, Loose, HiddenKeyed })
        {
            var rtId = record.CkRecordId.ToRtCkId();
            CkRecordGraph? ignored;
            A.CallTo(() => cache.TryGetRtCkRecord(Tenant,
                    A<RtCkId<CkRecordId>>.That.Matches(id => id != null && id.FullName == rtId.FullName), out ignored))
                .Returns(true).AssignsOutAndRefParameters(record);
            A.CallTo(() => cache.GetRtCkRecord(Tenant,
                    A<RtCkId<CkRecordId>>.That.Matches(id => id != null && id.FullName == rtId.FullName)))
                .Returns(record);
        }

        A.CallTo(() => cache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
            .Returns(new List<CkModelIdVersionRange>());
        return cache;
    }

    private sealed class Harness
    {
        public Harness()
        {
            CkCache = Cache();
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._))
                .Returns(Task.FromResult(new RtDataPolicyTable([])));
            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                    A<BulkOperationOptions>._))
                .Invokes(call => Written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
            A.CallTo(() => Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                    A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
                .ReturnsLazily(call =>
                {
                    var ids = call.GetArgument<IReadOnlyList<OctoObjectId>>(2)!;
                    var items = ids.Where(Stored.ContainsKey).Select(id => Stored[id]).ToList();
                    return Task.FromResult<IResultSet<RtEntity>>(new ResultSet<RtEntity>(items, items.Count, null, null));
                });
        }

        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = [];
        public ICkCacheService CkCache { get; }
        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public IRtYamlSerializer Yaml { get; } = A.Fake<IRtYamlSerializer>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public List<RtEntity> Written { get; } = [];

        public RtEntity Store(params (string Name, object? Value)[] attributes)
        {
            var entity = StoredHolder();
            entity.SetAttributeRawValue("Name", "stored");
            foreach (var (name, value) in attributes)
            {
                entity.SetAttributeRawValue(name, value);
            }

            Stored[entity.RtId] = entity;
            return entity;
        }

        public ImportRtModelCommand CreateCommand() => new(NullLogger<ImportRtModelCommand>.Instance, CkCache, Yaml,
            Json, A.Fake<IRtImportAuditTrail>(), new RtImportOptions(), Resolver, A.Fake<IAuditEventSink>());

        public void YamlReturns(params RtEntityTcDto[] entities)
        {
            var root = new RtModelRootTcDto { Entities = entities.ToList() };
            A.CallTo(() => Yaml.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
                .Returns(Task.FromResult(root));
        }
    }
}
