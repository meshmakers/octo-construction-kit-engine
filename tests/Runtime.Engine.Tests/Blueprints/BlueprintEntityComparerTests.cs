using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
///     AB#5297 - the update preview has to name what it would change. The comparer is the pure
///     core of that: seed entity against stored entity, per CK attribute, tenant-owned attributes
///     left out, values normalised the way the import reads them. The case that started this: a
///     seed identical to the tenant must produce NO changes, and a seed carrying
///     <c>Enabled: false</c> against a hand-enabled pipeline must produce exactly that one.
/// </summary>
public class BlueprintEntityComparerTests
{
    private const string Model = "Test-1.0.0";
    private static readonly CkId<CkEnumId> StateEnumId = new($"{Model}/State");
    private static readonly CkId<CkRecordId> ColumnRecordId = new($"{Model}/AggregationQueryColumn");
    private static readonly CkId<CkRecordId> DerivedColumnRecordId = new($"{Model}/DerivedQueryColumn");

    [Fact]
    public void IdenticalSeed_ReportsNoChange()
    {
        var type = BuildType(Attr("Enabled", AttributeValueTypesDto.Boolean), Attr("Name", AttributeValueTypesDto.String));
        var seed = Seed(("Enabled", true), ("Name", "Email Import"));
        var stored = Stored(("Enabled", true), ("Name", "Email Import"));

        var changes = Compare(seed, stored, type);

        Assert.Empty(changes);
    }

    [Fact]
    public void SeedFlippingAHandEnabledPipeline_IsTheOneChangeReported()
    {
        // The prod-1 case: three pipelines enabled by hand, the seed still says false.
        var type = BuildType(Attr("Enabled", AttributeValueTypesDto.Boolean), Attr("Name", AttributeValueTypesDto.String));
        var seed = Seed(("Enabled", false), ("Name", "Email Import"));
        var stored = Stored(("Enabled", true), ("Name", "Email Import"));

        var changes = Compare(seed, stored, type);

        var change = Assert.Single(changes);
        Assert.Equal("Enabled", change.AttributeName);
        Assert.Equal(true, change.OldValue);
        Assert.Equal(false, change.NewValue);
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.RuntimeState)]
    [InlineData(AttributeOwnershipDto.TenantOwned)]
    [InlineData(AttributeOwnershipDto.Secret)]
    public void AttributesTheTenantOwns_AreNeverReported(AttributeOwnershipDto ownership)
    {
        // The apply preserves these (PreserveAttributesForEntity), so the seed's value never lands.
        var type = BuildType(Attr("ApiKey", AttributeValueTypesDto.String, ownership));
        var seed = Seed(("ApiKey", "<placeholder>"));
        var stored = Stored(("ApiKey", "real-secret"));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void SeedOmittingAStoredAttribute_ReportsAClear()
    {
        // Upsert is a full replace: an attribute the seed no longer carries is wiped.
        var type = BuildType(Attr("Comment", AttributeValueTypesDto.String));
        var seed = Seed();
        var stored = Stored(("Comment", "set by an operator"));

        var change = Assert.Single(Compare(seed, stored, type));
        Assert.Equal("Comment", change.AttributeName);
        Assert.Equal("set by an operator", change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void EnumName_EqualsItsStoredKey()
    {
        // Seeds write names, the repository stores keys; the import resolves them the same way.
        var type = BuildType(EnumAttr("State"));
        var seed = Seed(("State", "Matched"));
        var stored = Stored(("State", 1));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void DateString_EqualsTheStoredUtcInstant()
    {
        var type = BuildType(Attr("StartDate", AttributeValueTypesDto.DateTime));
        var seed = Seed(("StartDate", "2026-01-01T00:00:00Z"));
        var stored = Stored(("StartDate", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void IntegralNumbers_CompareByValueNotByClrType()
    {
        // YAML hands the seed an int, the repository stores a long.
        var type = BuildType(Attr("Take", AttributeValueTypesDto.Integer64));
        var seed = Seed(("Take", 500));
        var stored = Stored(("Take", 500L));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void ADifferentString_IsAChange_WithBothValues()
    {
        var type = BuildType(Attr("PipelineDefinition", AttributeValueTypesDto.String));
        var seed = Seed(("PipelineDefinition", "new yaml"));
        var stored = Stored(("PipelineDefinition", "old yaml"));

        var change = Assert.Single(Compare(seed, stored, type));
        Assert.Equal("old yaml", change.OldValue);
        Assert.Equal("new yaml", change.NewValue);
    }

    [Fact]
    public void FreshBlueprintStamps_AreNotAChange()
    {
        // LoadAndTagSeedAsync stamps the target version and UtcNow onto every seed before the
        // comparison runs. Without this exclusion every locked entity reported two changes and
        // the preview was back at "137" - the review of AB#5297 caught it.
        var type = BuildType(
            Attr("RtBlueprintSource", AttributeValueTypesDto.String),
            Attr("RtBlueprintAppliedAt", AttributeValueTypesDto.DateTime),
            Attr("RtBlueprintLocked", AttributeValueTypesDto.Boolean),
            Attr("Name", AttributeValueTypesDto.String));
        var seed = Seed(("RtBlueprintSource", "MeshmakersAccounting-1.0.1"),
            ("RtBlueprintAppliedAt", DateTime.UtcNow), ("RtBlueprintLocked", true), ("Name", "x"));
        var stored = Stored(("RtBlueprintSource", "MeshmakersAccounting-1.0.0"),
            ("RtBlueprintAppliedAt", new DateTime(2026, 9, 16, 9, 45, 49, DateTimeKind.Utc)),
            ("RtBlueprintLocked", true), ("Name", "x"));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void SeedWithUntrimmedString_EqualsTheTrimmedStoredValue()
    {
        // The import trims on write (AttributeValueConverter); the stored value is the trimmed one.
        var type = BuildType(Attr("Name", AttributeValueTypesDto.String));
        var seed = Seed(("Name", "  Email Import  "));
        var stored = Stored(("Name", "Email Import"));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void StringArray_ComparesByElements_NotByListType()
    {
        var type = BuildType(Attr("Tags", AttributeValueTypesDto.StringArray));
        var seed = Seed(("Tags", new[] { "a", " b " }));
        var stored = Stored(("Tags", new List<object> { "a", "b" }));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void IdenticalRecords_AreNotAChange_AndDoNotNeedASerializer()
    {
        // The 3.4.124 prod-1 failure: the first version serialised RtRecordTcDto with the
        // default System.Text.Json options, whose converter attribute on CkRecordId is not
        // usable there, and the exception took the whole preview down.
        var type = BuildType(Attr("Address", AttributeValueTypesDto.Record));
        var seed = Seed(("Address", Record(("Street", "Firmianstr. 31A"), ("Zip", 5020))));
        var stored = Stored(("Address", Record(("Street", "Firmianstr. 31A"), ("Zip", 5020L))));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void RecordWithADifferentMember_IsAChange()
    {
        var type = BuildType(Attr("Address", AttributeValueTypesDto.Record));
        var seed = Seed(("Address", Record(("Street", "Rottmayrgasse 1"))));
        var stored = Stored(("Address", Record(("Street", "Firmianstr. 31A"))));

        var change = Assert.Single(Compare(seed, stored, type));
        Assert.Equal("Address", change.AttributeName);
    }

    [Fact]
    public void RecordArrays_CompareElementWise()
    {
        var type = BuildType(Attr("Lines", AttributeValueTypesDto.RecordArray));
        var seed = Seed(("Lines", new List<RtRecordTcDto> { Record(("Qty", 1)), Record(("Qty", 2)) }));
        var stored = Stored(("Lines", new List<RtRecordTcDto> { Record(("Qty", 1L)), Record(("Qty", 2L)) }));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void AdjacentDoubles_AreAChange()
    {
        // Routing doubles through decimal rounded both to the same 15 digits and hid the change.
        var type = BuildType(Attr("Factor", AttributeValueTypesDto.Double));
        var seed = Seed(("Factor", Math.BitIncrement(1.0)));
        var stored = Stored(("Factor", 1.0));

        Assert.Single(Compare(seed, stored, type));
    }

    [Fact]
    public void JsonContainers_FromDifferentDocuments_CompareByContent()
    {
        var a = System.Text.Json.JsonDocument.Parse("{\"street\":\"x\",\"tags\":[1,2]}").RootElement;
        var b = System.Text.Json.JsonDocument.Parse("{\"street\":\"x\",\"tags\":[1,2]}").RootElement;
        var c = System.Text.Json.JsonDocument.Parse("{\"street\":\"y\",\"tags\":[1,2]}").RootElement;

        Assert.True(BlueprintEntityComparer.ValuesEqual(a, b));
        Assert.False(BlueprintEntityComparer.ValuesEqual(a, c));
    }

    [Fact]
    public void JsonNull_EqualsClrNull()
    {
        var jsonNull = System.Text.Json.JsonDocument.Parse("null").RootElement;

        Assert.True(BlueprintEntityComparer.ValuesEqual(jsonNull, null));
        Assert.True(BlueprintEntityComparer.ValuesEqual(null, jsonNull));
        Assert.True(BlueprintEntityComparer.ValuesEqual(jsonNull, jsonNull));
    }

    [Fact]
    public void AThrowingConversion_IsReportedAsAChange_NotAsAFailure()
    {
        // ToTransportValue resolves record definitions through the CK cache; a stale record
        // throws there. The preview must survive that and report the attribute.
        var type = BuildType(Attr("Address", AttributeValueTypesDto.Record));
        var seed = Seed(("Address", Record(("Street", "x"))));
        var stored = Stored(("Address", new object()));

        var changes = BlueprintEntityComparer.Compare(seed, stored, type,
            _ => throw new InvalidOperationException("stale record definition"), ResolveEnum);

        var change = Assert.Single(changes);
        Assert.Equal("Address", change.AttributeName);
    }

    [Fact]
    public void ValuesEqual_NeverThrows_OnShapesItDoesNotKnow()
    {
        // Two distinct opaque instances: not equal, and above all no exception.
        Assert.False(BlueprintEntityComparer.ValuesEqual(new object(), new object()));
        Assert.False(BlueprintEntityComparer.ValuesEqual(new object(), "x"));
    }

    [Fact]
    public void AttributeNeitherSideHas_IsNotAChange()
    {
        var type = BuildType(Attr("Optional", AttributeValueTypesDto.String));

        Assert.Empty(Compare(Seed(), Stored(), type));
    }

    // ---- AB#5308: what the WRITE does, not what the seed says --------------------------------

    [Fact]
    public void SeedOmittingAnAttributeWithADefault_IsNotAChange()
    {
        // prod-1: the accounting seed declares neither Adapter.LifecycleMode (default 0) nor
        // IdleTimeoutMinutes (default 30). CreateTransientRtEntity pre-populates both and
        // AssignAttributes only touches declared attributes, so the stored value does not move -
        // the preview claimed it did.
        var type = BuildType(
            Defaulted("LifecycleMode", AttributeValueTypesDto.Integer, 0),
            Defaulted("IdleTimeoutMinutes", AttributeValueTypesDto.Integer, 30));
        var stored = Stored(("LifecycleMode", 0L), ("IdleTimeoutMinutes", 30L));

        Assert.Empty(Compare(Seed(), stored, type));
    }

    [Fact]
    public void SeedNullingAnAttributeWithADefault_ReportsTheClear()
    {
        // The opposite of omitting it: AssignAttributes writes what the seed declares, so a
        // declared null overwrites the pre-populated default and the value really is cleared.
        // (Review of AB#5308 - the first version of this fix had it the other way round, modelling
        // EntityRuleEngine, which the bulk import path bypasses entirely.)
        var type = BuildType(Defaulted("NavigationFilterMode", AttributeValueTypesDto.Integer, 0));

        var change = Assert.Single(
            Compare(Seed(("NavigationFilterMode", null)), Stored(("NavigationFilterMode", 0L)), type));
        Assert.Equal("NavigationFilterMode", change.AttributeName);
        Assert.Equal(0L, change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void SeedOmittingAMandatoryAttributeWithADefault_IsAChangeWhenTheTenantDivergedFromIt()
    {
        // The rule only says "the default lands", not "anything goes": an operator who moved the
        // value away from the default still has to see that the apply resets it.
        var type = BuildType(Defaulted("IdleTimeoutMinutes", AttributeValueTypesDto.Integer, 30));

        var change = Assert.Single(Compare(Seed(), Stored(("IdleTimeoutMinutes", 5L)), type));
        Assert.Equal("IdleTimeoutMinutes", change.AttributeName);
        Assert.Equal("IdleTimeoutMinutes", change.AttributeName);
        Assert.Equal(5L, change.OldValue);
        Assert.Equal(30L, change.NewValue);
    }

    [Fact]
    public void SeedOmittingAnOptionalAttributeWithADefault_IsNotAChangeEither()
    {
        // CreateTransientRtEntity pre-populates defaults for OPTIONAL attributes too, so
        // optionality does not enter into it - only "does the seed declare it".
        var type = BuildType(Defaulted("Mode", AttributeValueTypesDto.Integer, 0, isOptional: true));

        Assert.Empty(Compare(Seed(), Stored(("Mode", 0L)), type));
    }

    [Fact]
    public void SeedOmittingAnAttributeWithoutADefault_StillReportsTheClear()
    {
        // No default to inherit: the upsert is a full replace, so the stored value is cleared.
        var type = BuildType(Attr("Comment", AttributeValueTypesDto.String));

        var change = Assert.Single(Compare(Seed(), Stored(("Comment", "set by an operator")), type));
        Assert.Equal("set by an operator", change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void DefaultsMirrorTheTransientEntity_CollectionForStringArray_FirstEntryOtherwise()
    {
        // Parity with RuntimeRepositoryBase.CreateTransientRtEntity: StringArray and IntArray take
        // the whole defaultValues collection, every other type - RecordArray included - the first
        // entry. Getting this wrong would report a phantom on any seeded default.
        var stringArray = BuildType(DefaultedMany("Tags", AttributeValueTypesDto.StringArray, "a", "b"));
        Assert.Empty(Compare(Seed(), Stored(("Tags", new List<object?> { "a", "b" })), stringArray));

        var scalar = BuildType(DefaultedMany("Take", AttributeValueTypesDto.Integer64, 500, 900));
        Assert.Empty(Compare(Seed(), Stored(("Take", 500L)), scalar));
    }

    [Fact]
    public void RecordArrayNullInTheSeed_EqualsAStoredEmptyList()
    {
        // prod-1: five dashboard queries seed "Sorting: null" and store []. AssignAttributes
        // assigns its List<RtRecord> unconditionally, so null writes an empty list - same state.
        var type = BuildType(RecordArrayAttr("Sorting"));

        Assert.Empty(Compare(Seed(("Sorting", null)), Stored(("Sorting", new List<object?>())), type));
        Assert.Empty(Compare(Seed(), Stored(("Sorting", new List<object?>())), type));
    }

    [Fact]
    public void RecordArrayNullInTheSeed_IsAChangeWhenTheTenantHasEntries()
    {
        var type = BuildType(RecordArrayAttr("Sorting"));
        var stored = Stored(("Sorting", new List<object?> { Record(ColumnRecordId, ("AttributePath", "name")) }));

        var change = Assert.Single(Compare(Seed(("Sorting", null)), stored, type));
        Assert.Equal("Sorting", change.AttributeName);
    }

    [Fact]
    public void EnumMemberInsideARecord_EqualsItsStoredKey()
    {
        // prod-1: three aggregation queries seed the column's AggregationType as the text "1"
        // (and elsewhere as its name), the repository holds the key 1. The import resolves both
        // to the key, so neither is a change.
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("AttributePath", "netTotal"), ("State", 1L))
        }));

        foreach (var seedValue in new object[] { "1", 1, "Matched" })
        {
            var seed = Seed(("Columns", new List<object?>
            {
                Record(ColumnRecordId, ("AttributePath", "netTotal"), ("State", seedValue))
            }));

            Assert.Empty(Compare(seed, stored, type));
        }
    }

    [Fact]
    public void ADifferentEnumMemberInsideARecord_IsStillAChange()
    {
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("AttributePath", "netTotal"), ("State", 1L))
        }));
        var seed = Seed(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("AttributePath", "netTotal"), ("State", "Unreviewed"))
        }));

        Assert.Single(Compare(seed, stored, type));
    }

    [Fact]
    public void RecordMembersTheRecordDoesNotDeclare_CompareRaw()
    {
        // Unknown member (stale record definition): no conversion to apply, compare as-is rather
        // than claim equality.
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?> { Record(ColumnRecordId, ("Ghost", "a")) }));
        var seed = Seed(("Columns", new List<object?> { Record(ColumnRecordId, ("Ghost", "b")) }));

        Assert.Single(Compare(seed, stored, type));
    }

    [Fact]
    public void EnumMemberDeclaredOnlyOnADerivedRecord_EqualsItsStoredKey()
    {
        // Review of AB#5308: the attribute declares the BASE record, the tenant stores a DERIVED
        // one. Looking the member up in the declared graph misses anything the derived record adds,
        // and the comparison falls back to raw - a phantom again for exactly the enum case the fix
        // was about. The graph has to come from the instance.
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?>
        {
            Record(DerivedColumnRecordId, ("AttributePath", "netTotal"), ("DerivedState", 1L))
        }));
        var seed = Seed(("Columns", new List<object?>
        {
            Record(DerivedColumnRecordId, ("AttributePath", "netTotal"), ("DerivedState", "Matched"))
        }));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void ADifferentEnumMemberOnADerivedRecord_IsStillAChange()
    {
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?>
        {
            Record(DerivedColumnRecordId, ("DerivedState", 1L))
        }));
        var seed = Seed(("Columns", new List<object?>
        {
            Record(DerivedColumnRecordId, ("DerivedState", "Unreviewed"))
        }));

        Assert.Single(Compare(seed, stored, type));
    }

    [Fact]
    public void NestedRecordArrayNullInTheSeed_EqualsAStoredEmptyList()
    {
        // Review of AB#5308: the empty-list write rule applies one level down too - a RecordArray
        // MEMBER whose seed value is null lands as an empty list, same as a top-level one.
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("AttributePath", "netTotal"), ("Children", new List<object?>()))
        }));
        var seed = Seed(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("AttributePath", "netTotal"), ("Children", null))
        }));

        Assert.Empty(Compare(seed, stored, type));
    }

    [Fact]
    public void NestedRecordArrayNullInTheSeed_IsAChangeWhenTheTenantHasEntries()
    {
        var type = BuildType(RecordArrayAttr("Columns"));
        var stored = Stored(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("Children", new List<object?> { Record(ColumnRecordId, ("AttributePath", "x")) }))
        }));
        var seed = Seed(("Columns", new List<object?>
        {
            Record(ColumnRecordId, ("Children", null))
        }));

        Assert.Single(Compare(seed, stored, type));
    }

    // ---- helpers -------------------------------------------------------------------------

    private static List<Meshmakers.Octo.Runtime.Contracts.Blueprints.BlueprintAttributeChange> Compare(
        RtEntityTcDto seed, RtEntity stored, CkTypeGraph type)
    {
        return BlueprintEntityComparer.Compare(seed, stored, type, v => v, ResolveEnum, ResolveRecord);
    }

    /// <summary>
    ///     Two record graphs: the base one a record-valued attribute declares, and a DERIVED one
    ///     that adds a member of its own - the case the review of AB#5308 asked for, where the
    ///     attribute's declaration alone does not describe the stored instance.
    /// </summary>
    private static CkRecordGraph? ResolveRecord(RtCkId<CkRecordId> id)
    {
        // RtCkId renders without the model version, CkId with it - compare like for like.
        var wanted = id.ToString();

        if (string.Equals(wanted, ColumnRecordId.ToRtCkId().ToString(), StringComparison.Ordinal))
        {
            return RecordGraph(ColumnRecordId,
                Attr("AttributePath", AttributeValueTypesDto.String),
                EnumAttr("State"),
                RecordArrayAttr("Children"));
        }

        if (string.Equals(wanted, DerivedColumnRecordId.ToRtCkId().ToString(), StringComparison.Ordinal))
        {
            return RecordGraph(DerivedColumnRecordId,
                Attr("AttributePath", AttributeValueTypesDto.String),
                EnumAttr("State"),
                EnumAttr("DerivedState"),
                RecordArrayAttr("Children"));
        }

        return null;
    }

    private static CkRecordGraph RecordGraph(CkId<CkRecordId> recordId, params CkTypeAttributeGraph[] members)
    {
        return new CkRecordGraph(recordId, isAbstract: false, isFinal: false,
            baseRecords: [], derivedFromCkRecordId: null, derivedRecords: [], definedAttributes: [],
            allAttributes: members.ToDictionary(m => m.CkAttributeId, m => m),
            description: "test record");
    }

    private static CkEnumGraph? ResolveEnum(CkId<CkEnumId> id)
    {
        if (!id.Equals(StateEnumId))
        {
            return null;
        }

        return new CkEnumGraph(StateEnumId, new CkEnumDto
        {
            EnumId = "State",
            Values =
            [
                new CkEnumValueDto { Key = 0, Name = "Unreviewed" },
                new CkEnumValueDto { Key = 1, Name = "Matched" }
            ]
        });
    }

    private static RtRecordTcDto Record(params (string name, object? value)[] members)
    {
        return Record(new CkId<CkRecordId>($"{Model}/TestRecord"), members);
    }

    private static RtRecordTcDto Record(CkId<CkRecordId> recordId, params (string name, object? value)[] members)
    {
        var record = new RtRecordTcDto { CkRecordId = recordId.ToRtCkId() };
        foreach (var (name, value) in members)
        {
            record.Attributes.Add(new RtAttributeTcDto
            {
                Id = new CkId<CkAttributeId>($"{Model}/{name}").ToRtCkId(),
                Value = value
            });
        }

        return record;
    }

    private static RtEntityTcDto Seed(params (string name, object? value)[] attrs)
    {
        var dto = new RtEntityTcDto
        {
            RtId = new OctoObjectId("aa0000000000000000000601"),
            CkTypeId = new RtCkId<CkTypeId>($"{Model}/TestType")
        };
        foreach (var (name, value) in attrs)
        {
            dto.Attributes.Add(new RtAttributeTcDto
            {
                Id = new CkId<CkAttributeId>($"{Model}/{name}").ToRtCkId(),
                Value = value
            });
        }

        return dto;
    }

    private static RtEntity Stored(params (string name, object? value)[] attrs)
    {
        return new RtEntity(new RtCkId<CkTypeId>($"{Model}/TestType"), new OctoObjectId("aa0000000000000000000601"),
            attrs.ToDictionary(a => a.name, a => a.value));
    }

    private static CkTypeAttributeGraph Attr(string name, AttributeValueTypesDto valueType,
        AttributeOwnershipDto ownership = AttributeOwnershipDto.SeedOwned)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name, ValueType = valueType, Ownership = ownership
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, definition);
    }

    /// <summary>Attribute declaring one CK <c>defaultValues</c> entry.</summary>
    private static CkTypeAttributeGraph Defaulted(string name, AttributeValueTypesDto valueType, object defaultValue,
        bool isOptional = false)
    {
        return DefaultedMany(name, valueType, isOptional, defaultValue);
    }

    private static CkTypeAttributeGraph DefaultedMany(string name, AttributeValueTypesDto valueType,
        params object[] defaultValues)
    {
        return DefaultedMany(name, valueType, false, defaultValues);
    }

    private static CkTypeAttributeGraph DefaultedMany(string name, AttributeValueTypesDto valueType, bool isOptional,
        params object[] defaultValues)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name, ValueType = valueType, DefaultValues = [..defaultValues]
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = isOptional },
            definition);
    }

    /// <summary>RecordArray attribute pointing at the record <see cref="ResolveRecord" /> knows.</summary>
    private static CkTypeAttributeGraph RecordArrayAttr(string name)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name, ValueType = AttributeValueTypesDto.RecordArray, ValueCkRecordId = ColumnRecordId
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = true }, definition);
    }

    private static CkTypeAttributeGraph EnumAttr(string name)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = name, ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = StateEnumId
        });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, definition);
    }

    private static CkTypeGraph BuildType(params CkTypeAttributeGraph[] attrs)
    {
        return new CkTypeGraph(
            new CkId<CkTypeId>($"{Model}/TestType"),
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
}
