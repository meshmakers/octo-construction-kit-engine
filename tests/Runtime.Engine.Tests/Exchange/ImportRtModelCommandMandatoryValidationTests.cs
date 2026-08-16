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
