using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6386: the same-version rule of the publish gate compares the canonical form of the compiled model, not the
///     bytes. The source compiled by an older and a newer compiler (or restating a default) is a no-op with a warning
///     naming the difference class; a real content change stays <c>OCTO-CK205</c>.
/// </summary>
public sealed class CkPublishEquivalenceTests : IDisposable
{
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static CkCompiledModelRoot Model(string version = "2.4.0", string enumValues = "A,B") =>
        GateModels.Create(version, enumValues);

    /// <summary>The published form: no <c>ownership</c> marker (the older compiler did not stamp it).</summary>
    private static CkCompiledModelRoot Published() => Model();

    /// <summary>The same source with the default restated: <c>ownership: SeedOwned</c>.</summary>
    private static CkCompiledModelRoot Restated()
    {
        var model = Model();
        model.Attributes![0].Ownership = AttributeOwnershipDto.SeedOwned;
        return model;
    }

    private static string[] Warnings(CkPublishGateResult r) =>
        r.Messages.Where(m => m.Severity == CkCompileGateSeverity.Warning).Select(m => m.Text).ToArray();

    [Fact]
    public async Task Restated_default_ownership_is_a_no_op_with_a_warning_naming_the_class()
    {
        await _h.SeedAsync(Published());

        var result = await _h.EvaluateAsync(Restated());
        await _h.PublishAsync(Restated(), force: true);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.False(result.HasErrors);
        var warning = Assert.Single(Warnings(result));
        Assert.Contains("restated default ownership", warning);
        Assert.Contains("equivalent content", warning);
        Assert.Equal(0, _h.Stub.PublishCount); // nothing is written, the published file stays as it is
    }

    [Fact]
    public async Task Restated_ownership_next_to_a_real_content_change_is_still_CK205()
    {
        await _h.SeedAsync(Published());
        var changed = Restated();
        changed.Enums![0].Values!.Add(new CkEnumValueDto { Key = 2, Name = "C" });

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(changed));

        Assert.Contains("OCTO-CK205", exception.AllText());
        Assert.Equal(0, _h.Stub.PublishCount);
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.TenantOwned)]
    [InlineData(AttributeOwnershipDto.Secret)]
    public async Task A_changed_ownership_that_is_not_the_default_is_content_and_CK205(AttributeOwnershipDto ownership)
    {
        await _h.SeedAsync(Published());
        var changed = Model();
        changed.Attributes![0].Ownership = ownership;

        var result = await _h.EvaluateAsync(changed);

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK205"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task Flipping_the_runtime_state_alias_is_content_and_CK205()
    {
        await _h.SeedAsync(Published());
        var changed = Model();
        changed.Attributes![0].Ownership = AttributeOwnershipDto.RuntimeState; // published: SeedOwned by default

        var result = await _h.EvaluateAsync(changed);

        Assert.Equal(["OCTO-CK205"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task Restated_runtime_state_ownership_equals_the_isRuntimeState_alias()
    {
        var published = Model();
        published.Attributes![0].IsRuntimeState = true;
        await _h.SeedAsync(published);
        var restated = Model();
        restated.Attributes![0].Ownership = AttributeOwnershipDto.RuntimeState;

        var result = await _h.EvaluateAsync(restated);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.Contains("restated default ownership", Assert.Single(Warnings(result)));
    }

    [Fact]
    public async Task The_published_form_with_the_marker_and_the_new_form_without_it_are_equivalent_too()
    {
        await _h.SeedAsync(Restated());

        var result = await _h.EvaluateAsync(Published());

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
    }

    [Fact]
    public async Task A_different_element_order_is_equivalent()
    {
        var published = Model();
        published.Attributes!.Add(new CkAttributeDto { AttributeId = "Another", ValueType = AttributeValueTypesDto.String });
        await _h.SeedAsync(published);
        var reordered = Model();
        reordered.Attributes!.Insert(0, new CkAttributeDto { AttributeId = "Another", ValueType = AttributeValueTypesDto.String });

        var result = await _h.EvaluateAsync(reordered);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.Contains("element order", Assert.Single(Warnings(result)));
    }

    [Fact]
    public async Task A_different_compiler_stamped_minEngineVersion_is_equivalent()
    {
        var published = Model();
        published.MinEngineVersion = "3.5.0";
        await _h.SeedAsync(published);
        var current = Model();
        current.MinEngineVersion = "3.6.0";

        var result = await _h.EvaluateAsync(current);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.Contains("minEngineVersion", Assert.Single(Warnings(result)));
    }

    [Fact]
    public async Task Identical_content_keeps_the_plain_info_and_names_no_class()
    {
        await _h.SeedAsync(Published());

        var result = await _h.EvaluateAsync(Published());

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.Empty(Warnings(result));
        Assert.Contains(result.Messages, m => m.Text.Contains("identical content"));
    }

    [Fact]
    public async Task The_models_handed_to_the_comparison_are_not_modified()
    {
        var published = Published();
        var restated = Restated();

        var result = await new CkPublishEquivalence(_h.Get<Meshmakers.Octo.ConstructionKit.Contracts.Serialization.ICkJsonSerializer>())
            .CompareAsync(published, restated);

        Assert.True(result.AreEquivalent);
        Assert.Equal(AttributeOwnershipDto.SeedOwned, restated.Attributes![0].Ownership);
        Assert.Null(published.Attributes![0].Ownership);
    }

    private static CkCompiledModelRoot WithAssignment(AttributeOwnershipDto? ownership, AttributeOwnershipDto? definition = null)
    {
        var model = Model();
        model.Attributes![0].Ownership = definition;
        model.Types =
        [
            new CkCompiledTypeDto
            {
                TypeId = "Device",
                Attributes =
                [
                    new CkTypeAttributeDto
                    {
                        CkAttributeId = new CkId<CkAttributeId>("GateModel-2.4.0/Serial-1"),
                        AttributeName = "Serial",
                        Ownership = ownership
                    }
                ]
            }
        ];
        return model;
    }

    [Fact]
    public async Task An_assignment_that_restates_the_ownership_of_its_definition_is_equivalent()
    {
        await _h.SeedAsync(WithAssignment(null, AttributeOwnershipDto.TenantOwned));

        var result = await _h.EvaluateAsync(WithAssignment(AttributeOwnershipDto.TenantOwned, AttributeOwnershipDto.TenantOwned));

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
    }

    [Fact]
    public async Task An_assignment_that_overrides_the_ownership_of_its_definition_is_content()
    {
        await _h.SeedAsync(WithAssignment(null, AttributeOwnershipDto.TenantOwned));

        var result = await _h.EvaluateAsync(WithAssignment(AttributeOwnershipDto.SeedOwned, AttributeOwnershipDto.TenantOwned));

        Assert.Equal(["OCTO-CK205"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task On_the_lane_catalog_equivalent_content_is_a_no_op_not_a_replace()
    {
        using var lane = new GateHarness(lane: true);
        await lane.SeedAsync(Published());

        var result = await lane.EvaluateAsync(Restated());

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
    }
}
