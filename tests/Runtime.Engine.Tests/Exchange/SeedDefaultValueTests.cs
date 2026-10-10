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
///     AB#6395: an attribute the seed omits and that has a CK default is not blank - the import writes
///     the default again. Stored value equals the default: no finding at all. Differs: reason
///     <see cref="RtImportBlankingReason.ResetToDefault" />. No default: unchanged (<c>SeedOmitted</c>).
///     Preview (<see cref="SeedBlankingDetector" />) and apply (the import guard) agree.
/// </summary>
public class SeedDefaultValueTests
{
    private static readonly object[] ZeroDefault = [0L];

    [Theory]
    [InlineData(0)]
    [InlineData(0L)] // stored width differs from the default's: still equal
    public void Enum_AtDefault_OmittedBySeed_IsNoFinding(object stored)
    {
        var verdict = Decide(AttributeValueTypesDto.Enum, stored, ZeroDefault);

        Assert.Equal(SeedValueDecisionKind.TakeSeed, verdict.Kind);
        Assert.False(verdict.IsBlanking);
    }

    [Fact]
    public void Enum_OffDefault_OmittedBySeed_IsResetToDefault_KeptUnderKeep_AppliedUnderAllow()
    {
        var kept = Decide(AttributeValueTypesDto.Enum, 2, ZeroDefault);
        var allowed = Decide(AttributeValueTypesDto.Enum, 2, ZeroDefault, RtImportBlankingPolicy.Allow);

        Assert.Equal(RtImportBlankingReason.ResetToDefault, kept.Blanking);
        Assert.Equal(SeedValueDecisionKind.KeepExisting, kept.Kind);
        Assert.Equal(RtImportBlankingReason.ResetToDefault, allowed.Blanking);
        Assert.Equal(SeedValueDecisionKind.TakeSeed, allowed.Kind);
    }

    [Fact]
    public void String_WithDefault_AtAndOffDefault()
    {
        object[] defaults = ["standard"];

        Assert.False(Decide(AttributeValueTypesDto.String, "standard", defaults).IsBlanking);
        Assert.Equal(RtImportBlankingReason.ResetToDefault,
            Decide(AttributeValueTypesDto.String, "custom", defaults).Blanking);
    }

    [Fact]
    public void Boolean_WithDefault_AtAndOffDefault()
    {
        object[] defaults = [true];

        Assert.False(Decide(AttributeValueTypesDto.Boolean, true, defaults).IsBlanking);
        Assert.Equal(RtImportBlankingReason.ResetToDefault,
            Decide(AttributeValueTypesDto.Boolean, false, defaults).Blanking);
    }

    [Fact]
    public void Integer_WithDefault_AtAndOffDefault()
    {
        object[] defaults = [10];

        Assert.False(Decide(AttributeValueTypesDto.Integer, 10, defaults).IsBlanking);
        Assert.Equal(RtImportBlankingReason.ResetToDefault,
            Decide(AttributeValueTypesDto.Integer, 11, defaults).Blanking);
    }

    [Fact]
    public void StringArray_DefaultIsTheWholeCollection()
    {
        object[] defaults = ["a", "b"];

        Assert.False(Decide(AttributeValueTypesDto.StringArray, new List<string> { "a", "b" }, defaults).IsBlanking);
        Assert.Equal(RtImportBlankingReason.ResetToDefault,
            Decide(AttributeValueTypesDto.StringArray, new List<string> { "a" }, defaults).Blanking);
    }

    [Theory]
    [InlineData(AttributeValueTypesDto.Enum, 2)]
    [InlineData(AttributeValueTypesDto.Integer, 5)]
    [InlineData(AttributeValueTypesDto.Boolean, true)]
    [InlineData(AttributeValueTypesDto.String, "tenant-value")]
    public void WithoutDefault_OmittedBySeed_StaysSeedOmitted(AttributeValueTypesDto valueType, object stored)
    {
        var verdict = Decide(valueType, stored, null);

        Assert.Equal(RtImportBlankingReason.SeedOmitted, verdict.Blanking);
        Assert.Equal(SeedValueDecisionKind.KeepExisting, verdict.Kind);
    }

    [Fact]
    public void DeclaredEmptySeed_WithDefault_StaysSeedEmpty()
    {
        // A declared null is written as null, the default is gone: still a real blanking.
        var verdict = SeedValueGuard.Decide(AttributeValueTypesDto.String, AttributeOwnershipDto.SeedOwned, true,
            "standard", true, null, RtImportBlankingPolicy.Keep, ["standard"]);

        Assert.Equal(RtImportBlankingReason.SeedEmpty, verdict.Blanking);
    }

    [Fact]
    public void Preview_AndApply_AgreeOnDefaultedAttributes()
    {
        var attrs = new[]
        {
            TypeAttr("FilterMode", AttributeValueTypesDto.Enum, [0]), // at default: silent
            TypeAttr("Level", AttributeValueTypesDto.Enum, [0]), // off default: ResetToDefault
            TypeAttr("Note", AttributeValueTypesDto.String, null), // no default: SeedOmitted
        };
        var existing = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>("Test/TestType"),
        };
        existing.SetAttributeRawValue("FilterMode", 0);
        existing.SetAttributeRawValue("Level", 3);
        existing.SetAttributeRawValue("Note", "n");
        RtEntityTcDto NewSeed() => new() { RtId = existing.RtId, CkTypeId = existing.CkTypeId };

        var preview = SeedBlankingDetector.Detect(NewSeed(), existing, attrs)
            .Select(f => (f.Attribute.AttributeName, f.Reason)).OrderBy(x => x.AttributeName).ToList();
        var seedForApply = NewSeed();
        var apply = ImportRtModelCommand.GuardSeedOwnedAttributesForEntity(seedForApply, existing, attrs, v => v,
                RtImportBlankingPolicy.Keep)
            .Select(e => (e.AttributeName, e.Reason)).OrderBy(x => x.AttributeName).ToList();

        Assert.Equal(
            [("Level", RtImportBlankingReason.ResetToDefault), ("Note", RtImportBlankingReason.SeedOmitted)],
            preview);
        Assert.Equal(preview, apply);
        // The at-default enum is not touched by the guard (the import re-materialises the same default).
        Assert.DoesNotContain(seedForApply.Attributes, a => a.Id.ElementId.Name == "FilterMode");
    }

    [Fact]
    public void Summary_OfResetToDefault_NamesTheDefault_NeverTenantData()
    {
        var attr = TypeAttr("Level", AttributeValueTypesDto.Enum, [0]);
        var finding = new SeedBlankingFinding(attr, RtImportBlankingReason.ResetToDefault, 3, false, null, [0]);

        Assert.Equal("int32", SeedBlankingDetector.Summarize(finding.ExistingValue));
        Assert.Equal("default (0)", SeedBlankingDetector.SummarizeIncoming(finding));
        Assert.Equal("omitted",
            SeedBlankingDetector.SummarizeIncoming(finding with { Reason = RtImportBlankingReason.SeedOmitted }));
    }

    private static SeedValueVerdict Decide(AttributeValueTypesDto valueType, object? stored,
        ICollection<object>? defaults, RtImportBlankingPolicy policy = RtImportBlankingPolicy.Keep) =>
        SeedValueGuard.Decide(valueType, AttributeOwnershipDto.SeedOwned, existingPresent: true,
            existingValue: stored, seedPresent: false, seedValue: null, policy, defaults);

    private static CkTypeAttributeGraph TypeAttr(string name, AttributeValueTypesDto valueType,
        ICollection<object>? defaults)
    {
        var attrId = new CkId<CkAttributeId>($"Test-1.0.0/{name}");
        var ckAttrGraph = new CkAttributeGraph(attrId,
            new CkAttributeDto
            {
                AttributeId = name,
                ValueType = valueType,
                Ownership = AttributeOwnershipDto.SeedOwned,
                DefaultValues = defaults,
            });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name }, ckAttrGraph);
    }
}
