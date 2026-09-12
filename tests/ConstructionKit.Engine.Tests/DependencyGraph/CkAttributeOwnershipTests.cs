using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DependencyGraph;

/// <summary>
///     AB#5187 — the ownership model that replaces the <c>isRuntimeState</c> boolean.
///     The boolean answered two different questions with one bit ("preserve on Upsert?" and
///     "exclude from ExportRt?"), which collides for tenant master data that must be preserved AND
///     exported. These tests pin the three things the replacement has to get right:
///     the behaviour predicates, the deprecated alias mapping every existing model relies on
///     (zero behaviour change), and the per-assignment override.
/// </summary>
public class CkAttributeOwnershipTests
{
    private static CkId<CkAttributeId> AttrCkId(string name)
        => new($"Test-1.0.0/{name}");

    // ── Behaviour predicates: the two questions the boolean conflated ────────────────────

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned, false)]
    [InlineData(AttributeOwnershipDto.TenantOwned, true)]
    [InlineData(AttributeOwnershipDto.RuntimeState, true)]
    [InlineData(AttributeOwnershipDto.Secret, true)]
    public void IsPreservedOnUpsert_IsEverythingExceptSeedOwned(AttributeOwnershipDto ownership, bool expected)
    {
        Assert.Equal(expected, ownership.IsPreservedOnUpsert());
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned, false)]
    [InlineData(AttributeOwnershipDto.TenantOwned, false)]
    [InlineData(AttributeOwnershipDto.RuntimeState, true)]
    [InlineData(AttributeOwnershipDto.Secret, true)]
    public void IsExcludedFromExport_IsRuntimeStateAndSecretOnly(AttributeOwnershipDto ownership, bool expected)
    {
        Assert.Equal(expected, ownership.IsExcludedFromExport());
    }

    [Fact]
    public void TenantOwned_IsTheCombinationTheBooleanCouldNotExpress()
    {
        // The whole reason the enum exists: preserved on re-apply AND still portable.
        Assert.True(AttributeOwnershipDto.TenantOwned.IsPreservedOnUpsert());
        Assert.False(AttributeOwnershipDto.TenantOwned.IsExcludedFromExport());
    }

    // ── Deprecated alias: every declaration that exists today maps unchanged ─────────────

    [Fact]
    public void Resolve_UndeclaredAttribute_IsSeedOwned()
    {
        Assert.Equal(AttributeOwnershipDto.SeedOwned, AttributeOwnership.Resolve(null, isRuntimeState: false));
    }

    [Fact]
    public void Resolve_LegacyIsRuntimeStateTrue_IsRuntimeState()
    {
        Assert.Equal(AttributeOwnershipDto.RuntimeState, AttributeOwnership.Resolve(null, isRuntimeState: true));
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned)]
    [InlineData(AttributeOwnershipDto.TenantOwned)]
    [InlineData(AttributeOwnershipDto.RuntimeState)]
    [InlineData(AttributeOwnershipDto.Secret)]
    public void Resolve_DeclaredOwnership_WinsOverTheAlias(AttributeOwnershipDto ownership)
    {
        // Declaring both is an authoring error the lint rejects (OCTO-CK003); the engine still
        // resolves it deterministically instead of picking a coin-flip at runtime.
        Assert.Equal(ownership, AttributeOwnership.Resolve(ownership, isRuntimeState: true));
        Assert.Equal(ownership, AttributeOwnership.Resolve(ownership, isRuntimeState: false));
    }

    // ── CkAttributeDto: the alias becomes a computed mirror once ownership is declared ───

    [Fact]
    public void CkAttributeDto_WithoutOwnership_KeepsTheDeclaredBooleanVerbatim()
    {
        Assert.False(new CkAttributeDto { AttributeId = "A", ValueType = AttributeValueTypesDto.String }
            .IsRuntimeState);
        Assert.True(new CkAttributeDto
        {
            AttributeId = "A", ValueType = AttributeValueTypesDto.String, IsRuntimeState = true
        }.IsRuntimeState);
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned, false)]
    [InlineData(AttributeOwnershipDto.TenantOwned, true)]
    [InlineData(AttributeOwnershipDto.RuntimeState, true)]
    [InlineData(AttributeOwnershipDto.Secret, true)]
    public void CkAttributeDto_WithOwnership_MirrorsPreservedOnUpsertIntoTheAlias(
        AttributeOwnershipDto ownership, bool expectedMirror)
    {
        // The mirror is what makes version skew safe: it is serialised into the compiled model and
        // persisted by the CK-model repository, so an engine that does not know `ownership` reads a
        // TenantOwned or Secret attribute as isRuntimeState:true and degrades to preserve+exclude
        // rather than regressing to "seed wins", which would reset credentials.
        var dto = new CkAttributeDto
        {
            AttributeId = "A", ValueType = AttributeValueTypesDto.String, Ownership = ownership
        };

        Assert.Equal(expectedMirror, dto.IsRuntimeState);
    }

    [Fact]
    public void CkAttributeDto_OwnershipOverridesAnAlreadySetAlias()
    {
        var dto = new CkAttributeDto
        {
            AttributeId = "A",
            ValueType = AttributeValueTypesDto.String,
            IsRuntimeState = true,
            Ownership = AttributeOwnershipDto.SeedOwned
        };

        Assert.False(dto.IsRuntimeState);
    }

    // ── Propagation into the graphs the runtime consults ─────────────────────────────────

    [Fact]
    public void CkAttributeGraph_ResolvesOwnershipFromTheDefinition()
    {
        var graph = new CkAttributeGraph(AttrCkId("ApiKey"), new CkAttributeDto
        {
            AttributeId = "ApiKey", ValueType = AttributeValueTypesDto.String,
            Ownership = AttributeOwnershipDto.Secret
        });

        Assert.Equal(AttributeOwnershipDto.Secret, graph.Ownership);
        Assert.True(graph.IsRuntimeState);
    }

    [Fact]
    public void CkAttributeGraph_LegacyBooleanStillLandsAsRuntimeState()
    {
        var graph = new CkAttributeGraph(AttrCkId("DeploymentState"), new CkAttributeDto
        {
            AttributeId = "DeploymentState", ValueType = AttributeValueTypesDto.Enum, IsRuntimeState = true
        });

        Assert.Equal(AttributeOwnershipDto.RuntimeState, graph.Ownership);
    }

    [Fact]
    public void CkAttributeGraph_UnmarkedAttributeStaysSeedOwned()
    {
        var graph = new CkAttributeGraph(AttrCkId("Hostname"), new CkAttributeDto
        {
            AttributeId = "Hostname", ValueType = AttributeValueTypesDto.String
        });

        Assert.Equal(AttributeOwnershipDto.SeedOwned, graph.Ownership);
        Assert.False(graph.IsRuntimeState);
    }

    // ── Per-assignment override (product decision 3) ─────────────────────────────────────

    [Fact]
    public void Assignment_WithoutOverride_InheritsTheDefinition()
    {
        var graph = BuildAssignment(definition: AttributeOwnershipDto.Secret, assignment: null);

        Assert.Equal(AttributeOwnershipDto.Secret, graph.Ownership);
    }

    [Fact]
    public void Assignment_OverrideWins_SharedDefinitionCanBeSeedOwnedOnOneType()
    {
        // The concrete case: one shared `ClientId` definition is Secret because FinApi and
        // MicrosoftGraph need it frozen, but ServiceAccountConfiguration must stay renameable
        // by a blueprint. The assignment says so, next to the type it belongs to.
        var graph = BuildAssignment(definition: AttributeOwnershipDto.Secret,
            assignment: AttributeOwnershipDto.SeedOwned);

        Assert.Equal(AttributeOwnershipDto.SeedOwned, graph.Ownership);
        Assert.False(graph.IsRuntimeState);
    }

    [Fact]
    public void Assignment_CanTightenASeedOwnedDefinition()
    {
        var graph = BuildAssignment(definition: AttributeOwnershipDto.SeedOwned,
            assignment: AttributeOwnershipDto.TenantOwned);

        Assert.Equal(AttributeOwnershipDto.TenantOwned, graph.Ownership);
        Assert.True(graph.IsRuntimeState);
    }

    [Fact]
    public void Assignment_CanOverrideALegacyBooleanDefinition()
    {
        // An assignment-level override must work against a definition that still uses the
        // deprecated alias — otherwise the override would force a model to migrate both at once.
        var attrId = AttrCkId("ClientId");
        var definition = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = "ClientId", ValueType = AttributeValueTypesDto.String, IsRuntimeState = true
        });
        var assignment = new CkTypeAttributeDto
        {
            CkAttributeId = attrId, AttributeName = "ClientId", Ownership = AttributeOwnershipDto.SeedOwned
        };

        var graph = new CkTypeAttributeGraph(attrId, assignment, definition);

        Assert.Equal(AttributeOwnershipDto.SeedOwned, graph.Ownership);
    }

    [Fact]
    public void Assignment_DefaultsToNull_SoExistingModelsAreUntouched()
    {
        Assert.Null(new CkTypeAttributeDto { CkAttributeId = AttrCkId("X"), AttributeName = "X" }.Ownership);
    }

    private static CkTypeAttributeGraph BuildAssignment(AttributeOwnershipDto definition,
        AttributeOwnershipDto? assignment)
    {
        var attrId = AttrCkId("ClientId");
        var definitionGraph = new CkAttributeGraph(attrId, new CkAttributeDto
        {
            AttributeId = "ClientId", ValueType = AttributeValueTypesDto.String, Ownership = definition
        });
        var assignmentDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId, AttributeName = "ClientId", Ownership = assignment
        };

        return new CkTypeAttributeGraph(attrId, assignmentDto, definitionGraph);
    }
}
