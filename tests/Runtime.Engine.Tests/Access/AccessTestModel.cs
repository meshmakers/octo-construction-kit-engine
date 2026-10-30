using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Access;

/// <summary>
///     AB#7767: a self-made synthetic CK graph with every access value, so the export, import and blueprint tests
///     depend on no published model. Type <c>Test/Account</c> has Name (ReadWrite), Created (ReadOnly),
///     Email (MethodOnly), PasswordHash (Hidden), Profile (record, ReadWrite) and Credentials (record array,
///     ReadWrite); record <c>Test/Credential</c> has Label (ReadWrite) and Token (Hidden). <c>Test/Plain</c> has only
///     Name.
/// </summary>
internal static class AccessTestModel
{
    internal const string Tenant = "tenant-a";
    internal const string Model = "Test-1.0.0";

    internal static readonly RtCkId<CkTypeId> AccountType = new($"{Model}/Account");
    internal static readonly RtCkId<CkTypeId> PlainType = new($"{Model}/Plain");
    internal static readonly RtCkId<CkRecordId> CredentialRecord = new($"{Model}/Credential");

    internal static RtCkId<CkAttributeId> AttrId(string name) => new CkId<CkAttributeId>($"{Model}/{name}").ToRtCkId();

    private static CkTypeAttributeGraph Attr(string name, CkAttributeAccessDto? access,
        AttributeValueTypesDto valueType = AttributeValueTypesDto.String, CkId<CkRecordId>? record = null)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = name, ValueType = valueType, ValueCkRecordId = record });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = true, Access = access },
            definition);
    }

    private static readonly CkId<CkRecordId> CredentialRecordId = new($"{Model}/Credential");

    internal static CkTypeGraph AccountGraph() => Type("Account",
        Attr("Name", null),
        Attr("Created", CkAttributeAccessDto.ReadOnly),
        Attr("Email", CkAttributeAccessDto.MethodOnly),
        Attr("PasswordHash", CkAttributeAccessDto.Hidden),
        Attr("Profile", null, AttributeValueTypesDto.Record, CredentialRecordId),
        Attr("Credentials", null, AttributeValueTypesDto.RecordArray, CredentialRecordId));

    internal static CkTypeGraph PlainGraph() => Type("Plain", Attr("Name", null));

    internal static CkRecordGraph CredentialGraph()
    {
        var attrs = new[] { Attr("Label", null), Attr("Token", CkAttributeAccessDto.Hidden) };
        return new CkRecordGraph(CredentialRecordId, isAbstract: false, isFinal: false, baseRecords: [],
            derivedFromCkRecordId: null, derivedRecords: [], definedAttributes: [],
            allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a), description: "test");
    }

    private static CkTypeGraph Type(string name, params CkTypeAttributeGraph[] attrs) => new(
        new CkId<CkTypeId>($"{Model}/{name}"), isAbstract: false, isFinal: false, isCollectionRoot: false,
        baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [],
        definedAttributes: [], allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a), indexes: [],
        associations: new CkGraphDirectedAssociations([]), description: "test",
        enableChangeStreamPreAndPostImages: false);

    /// <summary>Wires the fake cache with both types and the record.</summary>
    /// <param name="withRestricted">False: the account type has only unrestricted attributes (no Hidden / MethodOnly).</param>
    internal static ICkCacheService Cache(ICkCacheService? cache = null, bool withRestricted = true)
    {
        cache ??= A.Fake<ICkCacheService>();
        var account = withRestricted ? AccountGraph() : PlainGraphAs("Account");
        var plain = PlainGraph();
        var record = withRestricted ? CredentialGraph() : null;
        var accountOut = (CkTypeGraph?)account;
        var plainOut = (CkTypeGraph?)plain;
        A.CallTo(() => cache.IsTenantLoaded(Tenant)).Returns(true);
        A.CallTo(() => cache.GetRtCkType(Tenant, AccountType)).Returns(account);
        A.CallTo(() => cache.GetRtCkType(Tenant, PlainType)).Returns(plain);
        A.CallTo(() => cache.TryGetRtCkType(Tenant, AccountType, out accountOut)).Returns(true)
            .AssignsOutAndRefParameters(account);
        A.CallTo(() => cache.TryGetRtCkType(Tenant, PlainType, out plainOut)).Returns(true)
            .AssignsOutAndRefParameters(plain);
        A.CallTo(() => cache.GetCkTypes(Tenant)).Returns(new List<CkTypeGraph> { account, plain });
        A.CallTo(() => cache.GetCkRecords(Tenant))
            .Returns(record == null ? new List<CkRecordGraph>() : new List<CkRecordGraph> { record });
        if (record != null)
        {
            var recordOut = (CkRecordGraph?)record;
            A.CallTo(() => cache.GetRtCkRecord(Tenant, CredentialRecord)).Returns(record);
            A.CallTo(() => cache.TryGetRtCkRecord(Tenant, CredentialRecord, out recordOut)).Returns(true)
                .AssignsOutAndRefParameters(record);
        }

        A.CallTo(() => cache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
            .Returns(new List<CkModelIdVersionRange>());
        return cache;
    }

    private static CkTypeGraph PlainGraphAs(string name) => Type(name, Attr("Name", null),
        Attr("Created", CkAttributeAccessDto.ReadOnly));

    internal static RtEntityTcDto Dto(RtCkId<CkTypeId> type, OctoObjectId? rtId = null,
        params (string Name, object? Value)[] attributes)
    {
        var dto = new RtEntityTcDto { RtId = rtId ?? OctoObjectId.GenerateNewId(), CkTypeId = type };
        foreach (var (name, value) in attributes)
        {
            dto.Attributes.Add(new RtAttributeTcDto { Id = AttrId(name), Value = value });
        }

        return dto;
    }

    internal static RtRecordTcDto RecordDto(params (string Name, object? Value)[] members)
    {
        var record = new RtRecordTcDto { CkRecordId = CredentialRecord };
        foreach (var (name, value) in members)
        {
            record.Attributes.Add(new RtAttributeTcDto { Id = AttrId(name), Value = value });
        }

        return record;
    }
}
