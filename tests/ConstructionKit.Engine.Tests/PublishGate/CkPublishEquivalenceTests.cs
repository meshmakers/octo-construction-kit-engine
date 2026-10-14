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

    // ---- AB#5435: the resolved closure of a range-retaining model (class 4) ----------------------------------------

    private static CkCompiledModelRoot RangeRetaining(string system = "2.5.0", string range = "[2.4,3.0)",
        string floor = "2.4.0", params string[] extraClosure)
    {
        var model = Model();
        model.Dependencies = [new CkModelId($"System-{system}"), .. extraClosure.Select(c => new CkModelId(c))];
        model.DependencyRanges = [new CkModelDependencyDto { Range = new CkModelIdVersionRange($"System-{range}"), Floor = floor }];
        return model;
    }

    private async Task SeedSystemsAsync()
    {
        foreach (var version in new[] { "2.4.0", "2.5.0", "2.6.1", "3.0.0" })
        {
            await _h.SeedAsync(GateModels.Create(version, name: "System"));
        }
    }

    [Fact]
    public async Task A_rebuild_after_a_dependency_minor_is_a_no_op_not_CK205_for_range_retaining_models()
    {
        await SeedSystemsAsync();
        // The story case: published against System 2.5.0, System 2.6.1 published since, rebuilt unchanged.
        await _h.SeedAsync(RangeRetaining("2.5.0"));
        var rebuilt = RangeRetaining("2.6.1");

        var result = await _h.EvaluateAsync(rebuilt);
        await _h.PublishAsync(rebuilt, force: true);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.False(result.HasErrors);
        var warning = Assert.Single(Warnings(result));
        Assert.Contains("resolved dependency closure of a range-retaining model", warning);
        Assert.Equal(0, _h.Stub.PublishCount); // the published file, and so the published closure, stays as it is
    }

    [Fact]
    public async Task On_a_dev_lane_catalog_a_closure_only_difference_is_still_a_no_op_not_a_replace()
    {
        using var lane = new GateHarness(lane: true);
        foreach (var version in new[] { "2.4.0", "2.5.0", "2.6.1" })
        {
            await lane.SeedAsync(GateModels.Create(version, name: "System"));
        }

        await lane.SeedAsync(RangeRetaining("2.5.0"));

        var result = await lane.EvaluateAsync(RangeRetaining("2.6.1"));

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.Contains("resolved dependency closure", Assert.Single(Warnings(result)));
    }

    [Fact]
    public async Task A_rebuild_against_an_older_closure_is_equivalent_too()
    {
        await SeedSystemsAsync();
        await _h.SeedAsync(RangeRetaining("2.6.1"));

        var result = await _h.EvaluateAsync(RangeRetaining("2.5.0"));

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
    }

    public static TheoryData<string> NotAMoveWithinTheRange() => new()
    {
        "major",         // System 3.0.0 is another major
        "range",         // declared range changed
        "floor",         // declared floor changed
        "name-added",    // a closure name appeared
        "name-removed",  // a closure name disappeared
        "outside-range", // the move leaves the effective range ([2.4,2.6) vs 2.6.1)
        "element"        // an unrelated element changed alongside the closure move
    };

    [Theory]
    [MemberData(nameof(NotAMoveWithinTheRange))]
    public async Task Everything_but_a_move_within_the_declared_range_stays_CK205(string kind)
    {
        await SeedSystemsAsync();
        await _h.SeedAsync(GateModels.Create("1.0.0", name: "Other"));
        var published = RangeRetaining("2.5.0", extraClosure: kind == "name-removed" ? ["Other-1.0.0"] : []);
        await _h.SeedAsync(published);
        var changed = kind switch
        {
            "major" => RangeRetaining("3.0.0"),
            "range" => RangeRetaining("2.6.1", range: "[2.4,3.0]"),
            "floor" => RangeRetaining("2.6.1", floor: "2.5.0"),
            "name-added" => RangeRetaining("2.6.1", extraClosure: ["Other-1.0.0"]),
            "name-removed" => RangeRetaining("2.6.1"),
            "outside-range" => RangeRetaining("2.6.1", range: "[2.4,2.6)"),
            _ => RangeRetaining("2.6.1")
        };
        if (kind == "element")
        {
            changed.Enums![0].Values!.Add(new CkEnumValueDto { Key = 2, Name = "C" });
        }

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(changed));

        Assert.Contains("OCTO-CK205", exception.AllText());
    }

    [Fact]
    public async Task A_closure_move_together_with_a_range_change_is_not_equivalent()
    {
        // A plain down move is equivalent in the equivalence (nothing resolves from the closure; the verdict still calls it
        // Major, D3, DependencyRangeRowTests); together with a changed range/floor it is content.
        await SeedSystemsAsync();
        await _h.SeedAsync(RangeRetaining("2.6.1", range: "[2.4,3.0)"));

        var result = await _h.EvaluateAsync(RangeRetaining("2.5.0", range: "[2.5,3.0)", floor: "2.5.0"));

        Assert.NotEqual(CkPublishGateDecision.AlreadyPublished, result.Decision);
    }

    [Fact]
    public async Task Exact_pinned_models_never_take_the_closure_path()
    {
        await SeedSystemsAsync();
        var published = Model();
        published.Dependencies = [new CkModelId("System-2.5.0")];
        await _h.SeedAsync(published);
        var rebuilt = Model();
        rebuilt.Dependencies = [new CkModelId("System-2.6.1")];

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(rebuilt));

        Assert.Contains("OCTO-CK205", exception.AllText());
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
