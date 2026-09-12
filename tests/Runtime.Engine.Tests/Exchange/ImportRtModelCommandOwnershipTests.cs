using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Engine.Exchange;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#5187 — the preservation half of the ownership model.
///     <c>ImportRtModelCommand.SelectPreservedAttributes</c> is the predicate that decides which
///     attributes an <c>Upsert</c> (blueprint re-apply, <c>ImportRt -r</c>, CK-migration writes)
///     must not overwrite with the seed's value. The question it answers is "who owns this value",
///     which is every ownership except <see cref="AttributeOwnershipDto.SeedOwned"/> — deliberately
///     wider than the export exclusion, which also drops <see cref="AttributeOwnershipDto.TenantOwned"/>
///     out of the equation. The per-entity copy loop itself is covered by
///     <see cref="ImportRtModelCommandPreserveAttributesForEntityTests"/>.
/// </summary>
public class ImportRtModelCommandOwnershipTests
{
    private const string TestCkModelId = "Test-1.0.0";

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned, false)]
    [InlineData(AttributeOwnershipDto.TenantOwned, true)]
    [InlineData(AttributeOwnershipDto.RuntimeState, true)]
    [InlineData(AttributeOwnershipDto.Secret, true)]
    public void SelectPreservedAttributes_PreservesEverythingTheTenantOwns(
        AttributeOwnershipDto ownership, bool expectedPreserved)
    {
        var graph = BuildType(BuildTypeAttr("Value", ownership));

        var preserved = ImportRtModelCommand.SelectPreservedAttributes(graph);

        Assert.Equal(expectedPreserved, preserved.Any(a => a.AttributeName == "Value"));
    }

    [Fact]
    public void SelectPreservedAttributes_LegacyBooleanBehavesExactlyAsBefore()
    {
        // Back-compat acceptance criterion: a model that only declares isRuntimeState selects the
        // same set it did before ownership existed — true is preserved, false and absent are not.
        var graph = BuildType(
            BuildLegacyTypeAttr("Status", isRuntimeState: true),
            BuildLegacyTypeAttr("ReportName", isRuntimeState: false),
            BuildLegacyTypeAttr("Unmarked", isRuntimeState: null));

        var preserved = ImportRtModelCommand.SelectPreservedAttributes(graph)
            .Select(a => a.AttributeName)
            .ToList();

        Assert.Equal(["Status"], preserved);
    }

    [Fact]
    public void SelectPreservedAttributes_TenantOwnedIsPreservedJustLikeASecret()
    {
        // The credential-reset incident and the reset-tariff incident get the same protection;
        // they differ only on the export axis.
        var graph = BuildType(
            BuildTypeAttr("TaxRate", AttributeOwnershipDto.TenantOwned),
            BuildTypeAttr("ApiKey", AttributeOwnershipDto.Secret),
            BuildTypeAttr("BillingReportName", AttributeOwnershipDto.SeedOwned));

        var preserved = ImportRtModelCommand.SelectPreservedAttributes(graph)
            .Select(a => a.AttributeName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["ApiKey", "TaxRate"], preserved);
    }

    [Fact]
    public void SelectPreservedAttributes_HonoursThePerAssignmentOverride()
    {
        // Shared Secret `ClientId` definition, but this type's assignment hands it back to the
        // seed so a blueprint can still rename the service account's client.
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
        var graph = BuildType(new CkTypeAttributeGraph(attrId, assignment, definition));

        Assert.Empty(ImportRtModelCommand.SelectPreservedAttributes(graph));
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

    private static CkTypeAttributeGraph BuildLegacyTypeAttr(string name, bool? isRuntimeState)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var dto = new CkAttributeDto { AttributeId = name, ValueType = AttributeValueTypesDto.String };
        if (isRuntimeState.HasValue)
        {
            dto.IsRuntimeState = isRuntimeState.Value;
        }

        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name },
            new CkAttributeGraph(attrId, dto));
    }
}
