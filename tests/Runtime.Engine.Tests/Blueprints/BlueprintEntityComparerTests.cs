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
    public void AttributeNeitherSideHas_IsNotAChange()
    {
        var type = BuildType(Attr("Optional", AttributeValueTypesDto.String));

        Assert.Empty(Compare(Seed(), Stored(), type));
    }

    // ---- helpers -------------------------------------------------------------------------

    private static List<Meshmakers.Octo.Runtime.Contracts.Blueprints.BlueprintAttributeChange> Compare(
        RtEntityTcDto seed, RtEntity stored, CkTypeGraph type)
    {
        return BlueprintEntityComparer.Compare(seed, stored, type, v => v, ResolveEnum);
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
