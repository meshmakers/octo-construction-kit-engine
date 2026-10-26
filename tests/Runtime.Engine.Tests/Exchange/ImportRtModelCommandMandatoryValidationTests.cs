using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
/// Unit tests for <see cref="ImportRtModelCommand.FindMissingMandatoryAttributes"/> — the
/// testable seam of the AB#4772 stage-2 import validation. The predicate must mirror the entity
/// rule engine's insert check exactly (not optional + no value + no default values + no
/// auto-increment reference), so an import warns/fails for precisely the entities an API insert
/// would have rejected (AB#4771: seeded RollupArchive entities without Archive.Columns).
/// </summary>
public class ImportRtModelCommandMandatoryValidationTests
{
    private const string TestCkModelId = "Test-1.0.0";
    private const string TestRtModelId = "Test";

    [Fact]
    public void MandatoryAttributeMissing_NoDefaultNoAutoIncrement_IsFlagged()
    {
        // The AB#4771 shape: Archive.Columns is mandatory with neither default nor
        // auto-increment, and the seed simply omits it.
        var attrs = new[] { BuildTypeAttr("Columns") };
        var entity = Entity();

        var missing = ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity);

        Assert.Single(missing);
        Assert.Equal("Columns", missing[0].AttributeName);
    }

    [Fact]
    public void MandatoryAttributePresent_IsNotFlagged()
    {
        var attrs = new[] { BuildTypeAttr("Columns") };
        var entity = Entity(("Columns", "some-value"));

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity));
    }

    [Fact]
    public void MandatoryAttributeWithNullValue_IsFlagged()
    {
        // An explicitly-null value is as absent as a missing key — the rule engine treats
        // both identically on insert.
        var attrs = new[] { BuildTypeAttr("Columns") };
        var entity = Entity(("Columns", null));

        Assert.Single(ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity));
    }

    [Fact]
    public void OptionalAttributeMissing_IsNotFlagged()
    {
        var attrs = new[] { BuildTypeAttr("RawRetentionMs", isOptional: true) };
        var entity = Entity();

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity));
    }

    [Fact]
    public void MandatoryAttributeWithDefaultValues_IsNotFlagged()
    {
        // Rule-engine parity: an API insert fills the default, so the attribute is not a
        // violation — even though the import path itself applies no defaults (deliberate
        // stage-2 scope decision, see the seam's doc comment).
        var attrs = new[] { BuildTypeAttr("Status", defaultValues: [0]) };
        var entity = Entity();

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity));
    }

    [Fact]
    public void MandatoryAttributeWithAutoIncrementReference_IsNotFlagged()
    {
        var attrs = new[] { BuildTypeAttr("Number", autoIncrementReference: "Test/DocNumber") };
        var entity = Entity();

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity));
    }

    [Fact]
    public void MixedAttributes_OnlyUnfillableMissingOnesAreFlagged()
    {
        var attrs = new[]
        {
            BuildTypeAttr("Columns"),                                       // missing, unfillable → flagged
            BuildTypeAttr("TargetCkTypeId"),                                // present → ok
            BuildTypeAttr("Status", defaultValues: [0]),                    // missing but default → ok
            BuildTypeAttr("RawRetentionMs", isOptional: true),              // optional → ok
        };
        var entity = Entity(("TargetCkTypeId", "Test/Target"));

        var missing = ImportRtModelCommand.FindMissingMandatoryAttributes(attrs, entity);

        Assert.Single(missing);
        Assert.Equal("Columns", missing[0].AttributeName);
    }

    // ---- AB#7450: required members inside records and RecordArrays ----

    private static readonly CkId<CkRecordId> DataPointRecordId = new($"{TestCkModelId}/DataPoint");
    private static readonly CkId<CkRecordId> OuterRecordId = new($"{TestCkModelId}/Outer");

    private static CkTypeAttributeGraph BuildRecordAttr(string name, AttributeValueTypesDto valueType,
        CkId<CkRecordId> recordId, bool isOptional = true)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var ckAttrGraph = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = name, ValueType = valueType, ValueCkRecordId = recordId });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = isOptional },
            ckAttrGraph);
    }

    private static CkRecordGraph BuildRecordGraph(CkId<CkRecordId> id, params CkTypeAttributeGraph[] members)
    {
        return new CkRecordGraph(id, false, false, [], null, [], [], members.ToDictionary(a => a.CkAttributeId, a => a),
            id.ToString());
    }

    // DataPoint = { ExternalId (required), CurrentValue (optional), LastUpdate (optional) } (the AB#7446 shape)
    private static CkRecordGraph DataPointRecord() => BuildRecordGraph(DataPointRecordId,
        BuildTypeAttr("ExternalId"),
        BuildTypeAttr("CurrentValue", isOptional: true),
        BuildTypeAttr("LastUpdate", isOptional: true));

    // Outer = { Inner : DataPoint (record, optional), Name (required) }
    private static CkRecordGraph OuterRecord() => BuildRecordGraph(OuterRecordId,
        BuildRecordAttr("Inner", AttributeValueTypesDto.Record, DataPointRecordId),
        BuildTypeAttr("Name"));

    private static CkRecordGraph? Resolve(RtCkId<CkRecordId> id)
    {
        var key = id.ToString();
        if (key.EndsWith("/DataPoint", StringComparison.Ordinal)) return DataPointRecord();
        if (key.EndsWith("/Outer", StringComparison.Ordinal)) return OuterRecord();
        return null;
    }

    private static RtRecord Record(CkId<CkRecordId> id, params (string Name, object? Value)[] attrs)
    {
        var record = new RtRecord { CkRecordId = id.ToRtCkId() };
        foreach (var (name, value) in attrs)
        {
            record.SetAttributeRawValue(name, value);
        }

        return record;
    }

    private static CkTypeAttributeGraph[] StatesAttr(AttributeValueTypesDto type, CkId<CkRecordId> recordId,
        string name = "States") => [BuildRecordAttr(name, type, recordId)];

    [Fact]
    public void RecordArray_ItemWithoutRequiredMember_IsFlaggedWithPath()
    {
        var attrs = StatesAttr(AttributeValueTypesDto.RecordArray, DataPointRecordId);
        var entity = Entity(("States", new List<RtRecord>
        {
            Record(DataPointRecordId, ("ExternalId", "a")),
            Record(DataPointRecordId, ("CurrentValue", 1)),              // ExternalId missing
            Record(DataPointRecordId, ("ExternalId", null)),             // null counts as missing
        }));

        var missing = ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, Resolve);

        Assert.Equal(
            [$"{TestCkModelId}/ExternalId-1 at States[1]", $"{TestCkModelId}/ExternalId-1 at States[2]"], missing);
    }

    [Fact]
    public void RecordArray_ItemsLackingOnlyOptionalMembers_ProduceNoViolation()
    {
        var attrs = StatesAttr(AttributeValueTypesDto.RecordArray, DataPointRecordId);
        var entity = Entity(("States", new List<RtRecord> { Record(DataPointRecordId, ("ExternalId", "a")) }));

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, Resolve));
    }

    [Fact]
    public void Record_WithoutRequiredMember_IsFlaggedWithPath()
    {
        var attrs = StatesAttr(AttributeValueTypesDto.Record, DataPointRecordId, "Primary");
        var entity = Entity(("Primary", Record(DataPointRecordId)));

        var missing = ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, Resolve);

        Assert.Equal([$"{TestCkModelId}/ExternalId-1 at Primary"], missing);
    }

    [Fact]
    public void NestedRecord_WithoutRequiredMember_IsFlaggedWithFullPath()
    {
        var attrs = StatesAttr(AttributeValueTypesDto.RecordArray, OuterRecordId, "Groups");
        var entity = Entity(("Groups", new List<RtRecord>
        {
            Record(OuterRecordId, ("Name", "ok"), ("Inner", Record(DataPointRecordId, ("ExternalId", "x")))),
            Record(OuterRecordId, ("Inner", Record(DataPointRecordId))),  // Name and Inner.ExternalId missing
        }));

        var missing = ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, Resolve);

        Assert.Equal(
        [
            $"{TestCkModelId}/Name-1 at Groups[1]",
            $"{TestCkModelId}/ExternalId-1 at Groups[1].Inner"
        ], missing);
    }

    [Fact]
    public void AbsentOrNullRecordValuedAttribute_IsNotInspected()
    {
        var attrs = new[]
        {
            BuildRecordAttr("Absent", AttributeValueTypesDto.Record, DataPointRecordId),
            BuildRecordAttr("NullRecord", AttributeValueTypesDto.Record, DataPointRecordId),
            BuildRecordAttr("NullArray", AttributeValueTypesDto.RecordArray, DataPointRecordId),
            BuildRecordAttr("EmptyArray", AttributeValueTypesDto.RecordArray, DataPointRecordId),
        };
        var entity = Entity(("NullRecord", null), ("NullArray", null), ("EmptyArray", new List<RtRecord>()));

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, Resolve));
    }

    [Fact]
    public void RecordMember_WithDefaultValues_IsNotFlagged()
    {
        var withDefault = BuildRecordGraph(DataPointRecordId, BuildTypeAttr("ExternalId", defaultValues: ["n/a"]));
        var attrs = StatesAttr(AttributeValueTypesDto.RecordArray, DataPointRecordId);
        var entity = Entity(("States", new List<RtRecord> { Record(DataPointRecordId) }));

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, _ => withDefault));
    }

    [Fact]
    public void TopLevelAndRecordViolations_AreReportedTogether_TopLevelWithoutPath()
    {
        var attrs = new[] { BuildTypeAttr("Columns"), BuildRecordAttr("States", AttributeValueTypesDto.RecordArray, DataPointRecordId) };
        var entity = Entity(("States", new List<RtRecord> { Record(DataPointRecordId) }));

        var missing = ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, Resolve);

        Assert.Equal([$"{TestCkModelId}/Columns-1", $"{TestCkModelId}/ExternalId-1 at States[0]"], missing);
    }

    [Fact]
    public void UnresolvableRecord_IsSkipped()
    {
        var attrs = StatesAttr(AttributeValueTypesDto.RecordArray, DataPointRecordId);
        var entity = Entity(("States", new List<RtRecord> { Record(DataPointRecordId) }));

        Assert.Empty(ImportRtModelCommand.FindMissingMandatoryMembers(attrs, entity, _ => null));
    }

    private static CkTypeAttributeGraph BuildTypeAttr(
        string name,
        bool isOptional = false,
        List<object>? defaultValues = null,
        string? autoIncrementReference = null)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var ckAttrDto = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = AttributeValueTypesDto.String,
            DefaultValues = defaultValues,
        };
        var ckAttrGraph = new CkAttributeGraph(attrId, ckAttrDto);
        var ckTypeAttrDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId,
            AttributeName = name,
            IsOptional = isOptional,
            AutoIncrementReference = autoIncrementReference,
        };
        return new CkTypeAttributeGraph(attrId, ckTypeAttrDto, ckAttrGraph);
    }

    private static RtEntity Entity(params (string Name, object? Value)[] attrs)
    {
        var entity = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>($"{TestRtModelId}/TestType"),
        };
        foreach (var (name, value) in attrs)
        {
            entity.SetAttributeRawValue(name, value);
        }

        return entity;
    }
}
