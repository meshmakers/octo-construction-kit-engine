using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#6313 (incident AB#6310): a blueprint update or re-apply never replaces a non-empty tenant
///     value with an empty, omitted or default seed value for an attribute the blueprint does not own.
///     <see cref="SeedValueGuard" /> is the pure rule; the entity-level tests drive the loop the import
///     runs for every Upsert.
/// </summary>
public class SeedValueGuardTests
{
    private const string TestCkModelId = "Test-1.0.0";
    private const string TestRtModelId = "Test";

    private static readonly string FilledConfiguration = PadTo(
        """{"EdaHttpAdapter":{"Host":"http://ponton.internal:8438","User":"edauser-0123456","Password":"pw-0123456789012345","IsSimulationMode":false}}""",
        223);

    private static readonly string EmptySkeletonConfiguration = PadTo(
        """{"EdaHttpAdapter":{"Host":"","User":"","Password":"","IsSimulationMode":true}}""", 110);

    private static string PadTo(string json, int length) => json.PadRight(length);

    // ---- pure rule -------------------------------------------------------------------------

    [Fact]
    public void FilledTenantValue_EmptySeedValue_IsKeptAndReported()
    {
        var verdict = Decide(AttributeValueTypesDto.String, "tenant-value", present: true, seedValue: "");

        Assert.Equal(SeedValueDecisionKind.KeepExisting, verdict.Kind);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, verdict.Blanking);
        Assert.True(verdict.IsBlanking);
    }

    [Fact]
    public void FilledTenantValue_NullSeedValue_IsKeptAndReported()
    {
        var verdict = Decide(AttributeValueTypesDto.String, "tenant-value", present: true, seedValue: null);

        Assert.Equal(SeedValueDecisionKind.KeepExisting, verdict.Kind);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, verdict.Blanking);
    }

    [Fact]
    public void FilledTenantValue_OmittedBySeed_IsKeptAndReported()
    {
        var verdict = Decide(AttributeValueTypesDto.String, "tenant-value", present: false, seedValue: null);

        Assert.Equal(SeedValueDecisionKind.KeepExisting, verdict.Kind);
        Assert.Equal(RtImportBlankingReason.SeedOmitted, verdict.Blanking);
    }

    [Fact]
    public void FilledTenantValue_DifferentNonEmptySeedValue_SeedWins()
    {
        var verdict = Decide(AttributeValueTypesDto.String, "old", present: true, seedValue: "new");

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Fact]
    public void EmptyTenantValue_SeedValueLands()
    {
        var verdict = Decide(AttributeValueTypesDto.String, "", present: true, seedValue: "seed");

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Fact]
    public void EmptyTenantValue_EmptySeedValue_NothingToLose()
    {
        var verdict = Decide(AttributeValueTypesDto.String, null, present: true, seedValue: "");

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Fact]
    public void AttributeAbsentOnExistingEntity_SeedValueLands()
    {
        var verdict = SeedValueGuard.Decide(AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned,
            existingPresent: false, existingValue: null, seedPresent: false, seedValue: null,
            RtImportBlankingPolicy.Keep);

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Fact]
    public void AllowPolicy_AppliesTheBlankingAndStillReportsIt()
    {
        var verdict = SeedValueGuard.Decide(AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned,
            existingPresent: true, existingValue: "tenant-value", seedPresent: true, seedValue: "",
            RtImportBlankingPolicy.Allow);

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, verdict.Blanking);
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.TenantOwned)]
    [InlineData(AttributeOwnershipDto.RuntimeState)]
    [InlineData(AttributeOwnershipDto.Secret)]
    public void NonSeedOwnedAttributes_KeepTheirExistingBehaviour_NeverReportedAsBlanking(
        AttributeOwnershipDto ownership)
    {
        // Preserved attributes are kept even against a non-empty seed value - that is the
        // existing preserve pass, not the blanking guard.
        var verdict = SeedValueGuard.Decide(AttributeValueTypesDto.String, ownership,
            existingPresent: true, existingValue: "tenant-value", seedPresent: true, seedValue: "seed",
            RtImportBlankingPolicy.Keep);

        Assert.Equal(SeedValueDecisionKind.KeepExisting, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Theory]
    [InlineData(AttributeValueTypesDto.Integer, 5, 0)]
    [InlineData(AttributeValueTypesDto.Int64, 5L, 0L)]
    [InlineData(AttributeValueTypesDto.Double, 1.5, 0.0)]
    [InlineData(AttributeValueTypesDto.Boolean, true, false)]
    [InlineData(AttributeValueTypesDto.Enum, 2, 0)]
    public void NumbersBooleansEnums_ExplicitDefaultInSeed_IsNotBlanking(
        AttributeValueTypesDto valueType, object existing, object seedDefault)
    {
        var verdict = Decide(valueType, existing, present: true, seedValue: seedDefault);

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Theory]
    [InlineData(AttributeValueTypesDto.Integer, 5)]
    [InlineData(AttributeValueTypesDto.Boolean, true)]
    [InlineData(AttributeValueTypesDto.Enum, 2)]
    public void NumbersBooleansEnums_OmittedBySeed_AreKept(AttributeValueTypesDto valueType, object existing)
    {
        var verdict = Decide(valueType, existing, present: false, seedValue: null);

        Assert.Equal(SeedValueDecisionKind.KeepExisting, verdict.Kind);
        Assert.Equal(RtImportBlankingReason.SeedOmitted, verdict.Blanking);
    }

    [Fact]
    public void Arrays_EmptySeedArrayOverFilledArray_IsKept_DifferentArrayWins()
    {
        var kept = Decide(AttributeValueTypesDto.StringArray, new List<string> { "a" }, present: true,
            seedValue: new List<string>());
        var replaced = Decide(AttributeValueTypesDto.StringArray, new List<string> { "a" }, present: true,
            seedValue: new List<string> { "b", "c" });

        Assert.Equal(SeedValueDecisionKind.KeepExisting, kept.Kind);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, kept.Blanking);
        Assert.Equal(SeedValueDecisionKind.TakeSeed, replaced.Kind);
        Assert.False(replaced.IsBlanking);
    }

    [Fact]
    public void Records_EmptySeedRecordOverFilledRecord_IsKept_FilledSeedRecordWins()
    {
        var existing = new RtRecord(RecordId(), new Dictionary<string, object?> { ["Host"] = "h", ["Port"] = 1 });

        var kept = Decide(AttributeValueTypesDto.Record, existing, present: true,
            seedValue: new RtRecordTcDto { CkRecordId = RecordId() });
        var keptEmptyMembers = Decide(AttributeValueTypesDto.Record, existing, present: true,
            seedValue: RecordTc(("Host", "")));
        var replaced = Decide(AttributeValueTypesDto.Record, existing, present: true,
            seedValue: RecordTc(("Host", "other")));

        Assert.Equal(SeedValueDecisionKind.KeepExisting, kept.Kind);
        Assert.Equal(SeedValueDecisionKind.KeepExisting, keptEmptyMembers.Kind);
        Assert.Equal(SeedValueDecisionKind.TakeSeed, replaced.Kind);
        Assert.False(replaced.IsBlanking);
    }

    [Fact]
    public void RecordArrays_EmptySeedArrayOverFilledRecordArray_IsKept()
    {
        var existing = new List<RtRecord>
        {
            new(RecordId(), new Dictionary<string, object?> { ["Path"] = "p" }),
        };

        var kept = Decide(AttributeValueTypesDto.RecordArray, existing, present: true,
            seedValue: new List<RtRecordTcDto>());
        var replaced = Decide(AttributeValueTypesDto.RecordArray, existing, present: true,
            seedValue: new List<RtRecordTcDto> { RecordTc(("Path", "q")) });

        Assert.Equal(SeedValueDecisionKind.KeepExisting, kept.Kind);
        Assert.Equal(SeedValueDecisionKind.TakeSeed, replaced.Kind);
    }

    [Fact]
    public void IsEmpty_Definition()
    {
        Assert.True(SeedValueGuard.IsEmpty(null));
        Assert.True(SeedValueGuard.IsEmpty(""));
        Assert.True(SeedValueGuard.IsEmpty(new List<int>()));
        Assert.False(SeedValueGuard.IsEmpty(" "));
        Assert.False(SeedValueGuard.IsEmpty(0));
        Assert.False(SeedValueGuard.IsEmpty(false));
        Assert.False(SeedValueGuard.IsEmpty(0.0));
        Assert.False(SeedValueGuard.IsEmpty(new byte[0]));
    }

    // ---- entity level (what the import runs) -------------------------------------------------

    [Fact]
    public void Entity_Ab6310_AdapterConfigurationShape_110CharSkeletonOver223CharValue_IsKept()
    {
        // The seed delivers valid JSON with empty host/user/password (110 chars); the tenant has the
        // real configuration (223 chars). The skeleton is not an empty string, but it blanks leaves.
        Assert.Equal(110, EmptySkeletonConfiguration.Length);
        Assert.Equal(223, FilledConfiguration.Length);
        var attrs = new[] { TypeAttr("AdapterConfiguration", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned) };
        var model = ModelEntity(("AdapterConfiguration", EmptySkeletonConfiguration));
        var existing = ExistingEntity(("AdapterConfiguration", FilledConfiguration));

        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, attrs,
            v => v, RtImportBlankingPolicy.Keep);

        Assert.Equal(FilledConfiguration, ValueOf(model, "AdapterConfiguration"));
        var entry = Assert.Single(entries);
        Assert.Equal("AdapterConfiguration", entry.AttributeName);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, entry.Reason);
        Assert.False(entry.Applied);
    }

    [Fact]
    public void Json_SeedChangesNonEmptyLeafOrDropsProperty_IsNotBlanking()
    {
        var existing = """{"Host":"h","User":"u","Extra":"e"}""";

        Assert.False(SeedValueGuard.BlanksJsonLeaf(existing, """{"Host":"other","User":"u2"}"""));
        Assert.False(SeedValueGuard.BlanksJsonLeaf(existing, """{"Host":"h"}"""));
        Assert.False(SeedValueGuard.BlanksJsonLeaf(existing, """{"Host":"h","Port":0,"Sim":true}"""));
        Assert.True(SeedValueGuard.BlanksJsonLeaf(existing, """{"Host":"h","User":""}"""));
        Assert.True(SeedValueGuard.BlanksJsonLeaf(existing, """{"Host":null}"""));
        Assert.True(SeedValueGuard.BlanksJsonLeaf("""{"A":[{"P":"x"}]}""", """{"A":[{"P":""}]}"""));
        Assert.False(SeedValueGuard.BlanksJsonLeaf("not json", """{"Host":""}"""));
        Assert.False(SeedValueGuard.BlanksJsonLeaf("""{"Host":""}""", """{"Host":""}"""));
    }

    [Fact]
    public void Entity_Ab6310_AdapterConfiguration_EmptyOrOmittedInSeed_KeepsFilledValue()
    {
        var attrs = new[] { TypeAttr("Configuration", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned) };
        var existing = ExistingEntity(("Configuration", FilledConfiguration));

        var emptyModel = ModelEntity(("Configuration", ""));
        var omittedModel = ModelEntity(("Name", "EDA Adapter"));

        var emptyEntries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(emptyModel, existing, attrs,
            v => v, RtImportBlankingPolicy.Keep);
        var omittedEntries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(omittedModel, existing, attrs,
            v => v, RtImportBlankingPolicy.Keep);

        Assert.Equal(FilledConfiguration, ValueOf(emptyModel, "Configuration"));
        Assert.Equal(FilledConfiguration, ValueOf(omittedModel, "Configuration"));
        var empty = Assert.Single(emptyEntries);
        Assert.Equal("Configuration", empty.AttributeName);
        Assert.Equal(RtImportBlankingReason.SeedEmpty, empty.Reason);
        Assert.False(empty.Applied);
        Assert.Equal(emptyModel.RtId, empty.RtId);
        Assert.Equal(RtImportBlankingReason.SeedOmitted, Assert.Single(omittedEntries).Reason);
    }

    [Fact]
    public void Entity_DifferentNonEmptySeedValue_ForBlueprintOwnedAttribute_IsReplaced()
    {
        var attrs = new[] { TypeAttr("Endpoint", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned) };
        var model = ModelEntity(("Endpoint", "https://new"));
        var existing = ExistingEntity(("Endpoint", "https://old"));

        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, attrs,
            v => v, RtImportBlankingPolicy.Keep);

        Assert.Empty(entries);
        Assert.Equal("https://new", ValueOf(model, "Endpoint"));
    }

    [Fact]
    public void Entity_AllowPolicy_LeavesTheModelAloneAndReportsAsApplied()
    {
        var attrs = new[] { TypeAttr("Configuration", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned) };
        var model = ModelEntity(("Configuration", ""));
        var existing = ExistingEntity(("Configuration", FilledConfiguration));

        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, attrs,
            v => v, RtImportBlankingPolicy.Allow);

        Assert.Equal("", ValueOf(model, "Configuration"));
        var entry = Assert.Single(entries);
        Assert.True(entry.Applied);
    }

    [Fact]
    public void Entity_RecordsAndArrays_KeptThroughTheConverter()
    {
        var attrs = new[]
        {
            TypeAttr("Overrides", AttributeValueTypesDto.RecordArray, AttributeOwnershipDto.SeedOwned),
            TypeAttr("Tags", AttributeValueTypesDto.StringArray, AttributeOwnershipDto.SeedOwned),
            TypeAttr("Limits", AttributeValueTypesDto.Record, AttributeOwnershipDto.SeedOwned),
        };
        var storedOverrides = new List<RtRecord>
            { new(RecordId(), new Dictionary<string, object?> { ["Path"] = "p" }) };
        var storedLimits = new RtRecord(RecordId(), new Dictionary<string, object?> { ["Max"] = 7 });
        var model = ModelEntity(("Overrides", new List<RtRecordTcDto>()), ("Tags", new List<string>()));
        var existing = ExistingEntity(("Overrides", storedOverrides), ("Tags", new List<string> { "a" }),
            ("Limits", storedLimits));

        var converted = new List<object?>();
        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, attrs,
            v => { converted.Add(v); return v; }, RtImportBlankingPolicy.Keep);

        Assert.Equal(3, entries.Count);
        Assert.Same(storedOverrides, ValueOf(model, "Overrides"));
        Assert.Equal(new List<string> { "a" }, ValueOf(model, "Tags"));
        // Omitted record attribute is injected, converted like every preserved value.
        Assert.Same(storedLimits, ValueOf(model, "Limits"));
        Assert.Equal(3, converted.Count);
    }

    [Fact]
    public void Entity_NewEntityOrNewAttribute_IsUntouched()
    {
        var attrs = new[] { TypeAttr("Configuration", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned) };
        var model = ModelEntity(("Configuration", ""));
        var existing = ExistingEntity(); // attribute added in this CK bump: no stored value

        var entries = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(model, existing, attrs,
            v => v, RtImportBlankingPolicy.Keep);

        Assert.Empty(entries);
        Assert.Equal("", ValueOf(model, "Configuration"));
    }

    [Fact]
    public void SelectGuardedAttributes_IsTheComplementOfSelectPreservedAttributes()
    {
        var graph = BuildType(
            TypeAttr("Seed", AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned),
            TypeAttr("Tenant", AttributeValueTypesDto.String, AttributeOwnershipDto.TenantOwned),
            TypeAttr("Runtime", AttributeValueTypesDto.String, AttributeOwnershipDto.RuntimeState),
            TypeAttr("Secret", AttributeValueTypesDto.String, AttributeOwnershipDto.Secret));

        var guarded = ImportRtModelCommand.SelectGuardedAttributes(graph).Select(a => a.AttributeName).ToList();
        var preserved = ImportRtModelCommand.SelectPreservedAttributes(graph).Select(a => a.AttributeName).ToList();

        Assert.Equal(new[] { "Seed" }, guarded);
        Assert.Equal(3, preserved.Count);
        Assert.Empty(guarded.Intersect(preserved));
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static SeedValueVerdict Decide(AttributeValueTypesDto valueType, object? existing, bool present,
        object? seedValue)
    {
        return SeedValueGuard.Decide(valueType, AttributeOwnershipDto.SeedOwned, existingPresent: true,
            existingValue: existing, seedPresent: present, seedValue: seedValue, RtImportBlankingPolicy.Keep);
    }

    private static RtCkId<CkRecordId> RecordId() => new($"{TestRtModelId}/Rec");

    private static RtRecordTcDto RecordTc(params (string Name, object? Value)[] attrs)
    {
        var record = new RtRecordTcDto { CkRecordId = RecordId() };
        foreach (var (name, value) in attrs)
        {
            record.Attributes.Add(new RtAttributeTcDto
            {
                Id = new RtCkId<CkAttributeId>($"{TestRtModelId}/{name}"),
                Value = value,
            });
        }

        return record;
    }

    private static object? ValueOf(RtEntityTcDto model, string name)
    {
        return model.Attributes.Single(a => a.Id.ElementId.Name == name).Value;
    }

    private static CkTypeAttributeGraph TypeAttr(string name, AttributeValueTypesDto valueType,
        AttributeOwnershipDto ownership)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var ckAttrDto = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = valueType,
            Ownership = ownership,
        };
        var ckAttrGraph = new CkAttributeGraph(attrId, ckAttrDto);
        var ckTypeAttrDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId,
            AttributeName = name,
        };
        return new CkTypeAttributeGraph(attrId, ckTypeAttrDto, ckAttrGraph);
    }

    private static RtEntityTcDto ModelEntity(params (string Name, object? Value)[] attrs)
    {
        var entity = new RtEntityTcDto
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>($"{TestRtModelId}/TestType"),
        };
        foreach (var (name, value) in attrs)
        {
            entity.Attributes.Add(new RtAttributeTcDto
            {
                Id = new RtCkId<CkAttributeId>($"{TestRtModelId}/{name}"),
                Value = value,
            });
        }

        return entity;
    }

    private static RtEntity ExistingEntity(params (string Name, object? Value)[] attrs)
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

    private static CkTypeGraph BuildType(params CkTypeAttributeGraph[] attrs)
    {
        return new CkTypeGraph(
            new CkId<CkTypeId>($"{TestCkModelId}/TestType"),
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
