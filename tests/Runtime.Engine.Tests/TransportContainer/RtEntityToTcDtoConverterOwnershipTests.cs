using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.TransportContainer;

using FakeItEasy;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.TransportContainer;

/// <summary>
///     AB#5187 — the export half of the ownership model.
///     <see cref="RtEntityToTcDtoConverter"/> is the only consumer of the export exclusion (its
///     interface has exactly two callers, both <c>ExportRt</c> commands in octo-bot-services), so
///     this is the single place the portability question is answered. The behaviour that changes
///     is <see cref="AttributeOwnershipDto.TenantOwned"/>: it is preserved on Upsert like the old
///     boolean but is now exported, because a tariff, an IBAN, a market-partner id or a logo is
///     part of the entity's portable definition. Dropping those is what broke tenant clone /
///     "export as template" and it is the reason the boolean had to go.
/// </summary>
public class RtEntityToTcDtoConverterOwnershipTests
{
    private const string TestCkModelId = "System.Communication-1.0.0";
    private const string TestRtModelId = "System.Communication";
    private const string TenantId = "tenant-1";

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned, true)]
    [InlineData(AttributeOwnershipDto.TenantOwned, true)]
    [InlineData(AttributeOwnershipDto.RuntimeState, false)]
    [InlineData(AttributeOwnershipDto.Secret, false)]
    public void Convert_ExportsByOwnership(AttributeOwnershipDto ownership, bool expectedExported)
    {
        var attr = BuildTypeAttr("Value", ownership);
        var converter = new RtEntityToTcDtoConverter(FakeCacheReturning(BuildType(attr)));

        var dto = converter.Convert(TenantId, Entity(("Value", "x")));

        Assert.Equal(expectedExported, dto.Attributes.Any(a => a.Id.ElementId.Name == "Value"));
    }

    [Fact]
    public void Convert_LegacyBooleanAttributes_BehaveExactlyAsBefore()
    {
        // Back-compat acceptance criterion: a model that only declares isRuntimeState must produce
        // the same export as it did before ownership existed.
        var converter = new RtEntityToTcDtoConverter(FakeCacheReturning(BuildType(
            BuildLegacyTypeAttr("Name", isRuntimeState: false),
            BuildLegacyTypeAttr("CommunicationStateTimestamp", isRuntimeState: true))));

        var dto = converter.Convert(TenantId, Entity(
            ("Name", "meshtest Adapter"),
            ("CommunicationStateTimestamp", new DateTime(2026, 5, 18, 0, 2, 1, DateTimeKind.Utc))));

        var exported = dto.Attributes.Select(a => a.Id.ElementId.Name).ToList();
        Assert.Contains("Name", exported);
        Assert.DoesNotContain("CommunicationStateTimestamp", exported);
    }

    [Fact]
    public void Convert_TenantOwnedCredentialTuplePartner_IsExportedWhileTheSecretIsNot()
    {
        // The FinApiConfiguration shape the item was raised for: the sandbox flag and the host
        // travel with the tuple and belong in an export, the secret never does.
        var converter = new RtEntityToTcDtoConverter(FakeCacheReturning(BuildType(
            BuildTypeAttr("IsSandbox", AttributeOwnershipDto.TenantOwned),
            BuildTypeAttr("ClientSecret", AttributeOwnershipDto.Secret))));

        var dto = converter.Convert(TenantId, Entity(("IsSandbox", "true"), ("ClientSecret", "s3cr3t")));

        var exported = dto.Attributes.Select(a => a.Id.ElementId.Name).ToList();
        Assert.Equal(["IsSandbox"], exported);
    }

    [Fact]
    public void Convert_AssignmentOverrideDecidesTheExport_NotTheSharedDefinition()
    {
        // One shared Secret `ClientId` definition, assigned by a type that overrides it to
        // SeedOwned: the override must reach the export decision, or the override would only be
        // half-implemented (preserved correctly, still stripped from the export).
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/ClientId");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = "ClientId",
            ValueType = AttributeValueTypesDto.String,
            Ownership = AttributeOwnershipDto.Secret
        });
        var assignment = new CkTypeAttributeDto
        {
            CkAttributeId = attrId, AttributeName = "ClientId", Ownership = AttributeOwnershipDto.SeedOwned
        };
        var converter = new RtEntityToTcDtoConverter(
            FakeCacheReturning(BuildType(new CkTypeAttributeGraph(attrId, assignment, definition))));

        var dto = converter.Convert(TenantId, Entity(("ClientId", "service-account")));

        Assert.Contains(dto.Attributes, a => a.Id.ElementId.Name == "ClientId");
    }

    [Fact]
    public void Convert_RecordMembers_AreEvaluatedIndividually()
    {
        // Deliberate granularity decision, documented in the converter: export recurses into
        // records and skips members one by one, unlike upsert preservation which only inspects
        // top-level type attributes. A TenantOwned record whose members were left RuntimeState by
        // an older sweep therefore exports with those members stripped — an authoring trap, not an
        // engine bug, and this test pins the behaviour so the trap is visible.
        var recordId = new CkId<CkRecordId>($"{TestCkModelId}/UiThemeColors");
        var recordGraph = BuildRecord(recordId,
            BuildTypeAttr("PrimaryColor", AttributeOwnershipDto.TenantOwned),
            BuildTypeAttr("InternalSeed", AttributeOwnershipDto.RuntimeState));

        var colorsAttr = BuildRecordValuedTypeAttr("UiThemeColors", recordId, AttributeOwnershipDto.TenantOwned);
        var ckCacheService = FakeCacheReturning(BuildType(colorsAttr));
        A.CallTo(() => ckCacheService.GetRtCkRecord(A<string>._, A<RtCkId<CkRecordId>>._)).Returns(recordGraph);
        var converter = new RtEntityToTcDtoConverter(ckCacheService);

        var record = new RtRecord { CkRecordId = new RtCkId<CkRecordId>($"{TestRtModelId}/UiThemeColors") };
        record.SetAttributeRawValue("PrimaryColor", "#ff9900");
        record.SetAttributeRawValue("InternalSeed", "42");
        var dto = converter.Convert(TenantId, Entity(("UiThemeColors", record)));

        var exportedRecord = Assert.IsType<RtRecordTcDto>(
            dto.Attributes.Single(a => a.Id.ElementId.Name == "UiThemeColors").Value);
        var memberNames = exportedRecord.Attributes.Select(a => a.Id.ElementId.Name).ToList();
        Assert.Contains("PrimaryColor", memberNames);
        Assert.DoesNotContain("InternalSeed", memberNames);
    }

    private static ICkCacheService FakeCacheReturning(CkTypeGraph graph)
    {
        var ckCacheService = A.Fake<ICkCacheService>();
        A.CallTo(() => ckCacheService.GetRtCkType(A<string>._, A<RtCkId<CkTypeId>>._)).Returns(graph);
        return ckCacheService;
    }

    private static RtEntity Entity(params (string Name, object Value)[] attrs)
    {
        var entity = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>($"{TestRtModelId}/Adapter")
        };
        foreach (var (name, value) in attrs)
        {
            entity.SetAttributeRawValue(name, value);
        }

        return entity;
    }

    private static CkTypeGraph BuildType(params CkTypeAttributeGraph[] attrs)
    {
        return new CkTypeGraph(
            new CkId<CkTypeId>($"{TestCkModelId}/Adapter"),
            isAbstract: false,
            isFinal: false,
            isCollectionRoot: false,
            baseTypes: [],
            derivedFromCkTypeId: null,
            definingCollectionRootCkTypeId: null,
            derivedTypes: [],
            definedAttributes: [],
            allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a),
            indexes: [],
            associations: new CkGraphDirectedAssociations([]),
            description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private static CkRecordGraph BuildRecord(CkId<CkRecordId> recordId, params CkTypeAttributeGraph[] attrs)
    {
        return new CkRecordGraph(
            recordId,
            isAbstract: false,
            isFinal: false,
            baseRecords: [],
            derivedFromCkRecordId: null,
            derivedRecords: [],
            definedAttributes: [],
            allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a),
            description: "test");
    }

    private static CkTypeAttributeGraph BuildTypeAttr(string name, AttributeOwnershipDto ownership)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name, ValueType = AttributeValueTypesDto.String, Ownership = ownership
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, definition);
    }

    private static CkTypeAttributeGraph BuildRecordValuedTypeAttr(string name, CkId<CkRecordId> recordId,
        AttributeOwnershipDto ownership)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name,
            ValueType = AttributeValueTypesDto.Record,
            ValueCkRecordId = recordId,
            Ownership = ownership
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, definition);
    }

    private static CkTypeAttributeGraph BuildLegacyTypeAttr(string name, bool isRuntimeState)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name, ValueType = AttributeValueTypesDto.String, IsRuntimeState = isRuntimeState
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, definition);
    }
}
