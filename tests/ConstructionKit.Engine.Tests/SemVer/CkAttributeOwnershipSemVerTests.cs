using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     AB#5187 — how the diff and the SemVer rule table see ownership.
///     Two properties matter here, and the second one is the reason the diff compares RESOLVED
///     values rather than raw ones: a genuine ownership change owes a Minor bump (without it
///     <c>ImportCkModelAsync</c> short-circuits the same-version re-import and the marker reaches no
///     existing tenant), while merely RENAMING a declaration from the deprecated boolean to the
///     equivalent enum value means nothing changed and must cost nothing. Otherwise every one of
///     the 33 CK models would owe a bump the first time it recompiles against this engine.
/// </summary>
public class CkAttributeOwnershipSemVerTests
{
    private readonly CkModelDiffService _diffService = new();
    private readonly CkSemVerClassifier _classifier = new();

    private IReadOnlyList<CkModelChange> Diff(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
        => _diffService.Diff(baseline, current);

    private CkSemVerLevel ClassifyHighest(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        var changes = Diff(baseline, current);
        Assert.NotEmpty(changes);
        return _classifier.GetRequiredLevel(_classifier.Classify(changes, baseline, current));
    }

    // ── A real ownership change is Minor ────────────────────────────────────────────────

    [Theory]
    [InlineData(AttributeOwnershipDto.TenantOwned)]
    [InlineData(AttributeOwnershipDto.RuntimeState)]
    [InlineData(AttributeOwnershipDto.Secret)]
    public void OwnershipDeclaredOnAnUnmarkedAttribute_IsMinor(AttributeOwnershipDto ownership)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").Ownership = ownership;

        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void OwnershipChangedBetweenTwoNonSeedValues_IsMinor()
    {
        // RuntimeState → Secret: identical behaviour today, but it is a declaration change and the
        // model must reach existing tenants, so it still owes a bump.
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "StateAttr").Ownership = AttributeOwnershipDto.RuntimeState;
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").Ownership = AttributeOwnershipDto.Secret;

        var changes = Diff(baseline, current);
        Assert.Contains(changes, c => c is { ElementKind: CkModelElementKind.Attribute, Property: "ownership" });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void RuntimeStateToTenantOwned_IsMinor_AndIsTheReclassificationThatMatters()
    {
        // The step-3 sweep: an attribute that was `isRuntimeState: true` because the tenant owns it
        // becomes TenantOwned, which brings it back into ExportRt. Behaviour changes, so it bumps.
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "StateAttr").IsRuntimeState = true;
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").Ownership = AttributeOwnershipDto.TenantOwned;

        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    // ── Migrating the declaration without changing its meaning is free ──────────────────

    [Fact]
    public void LegacyTrueRenamedToRuntimeState_IsNotAChange()
    {
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "StateAttr").IsRuntimeState = true;
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").Ownership = AttributeOwnershipDto.RuntimeState;

        Assert.Empty(Diff(baseline, current));
    }

    [Fact]
    public void LegacyFalseRenamedToSeedOwned_IsNotAChange()
    {
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "StateAttr").IsRuntimeState = false;
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").Ownership = AttributeOwnershipDto.SeedOwned;

        Assert.Empty(Diff(baseline, current));
    }

    [Fact]
    public void UnmarkedAttributeDeclaredSeedOwned_IsNotAChange()
    {
        // Taking an explicit position that matches the implicit default is documentation, not a
        // behaviour change — which is what lets a project arm the lint without a version bump.
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").Ownership = AttributeOwnershipDto.SeedOwned;

        Assert.Empty(Diff(baseline, current));
    }

    [Fact]
    public void UnchangedModel_ProducesNoOwnershipNoise()
    {
        Assert.Empty(Diff(SemVerTestModels.CreateModel(), SemVerTestModels.CreateModel()));
    }

    // ── The per-assignment override ─────────────────────────────────────────────────────

    [Fact]
    public void AssignmentOverrideAdded_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        GetAssignment(current, "State").Ownership = AttributeOwnershipDto.Secret;

        var changes = Diff(baseline, current);
        Assert.Contains(changes, c => c is { ElementKind: CkModelElementKind.TypeAttribute, Property: "ownership" });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AssignmentOverrideRemoved_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        GetAssignment(baseline, "State").Ownership = AttributeOwnershipDto.Secret;
        var current = SemVerTestModels.CreateModel();

        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AssignmentWithoutOverride_ProducesNoChange()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        Assert.DoesNotContain(Diff(baseline, current), c => c.Property == "ownership");
    }

    private static CkTypeAttributeDto GetAssignment(CkCompiledModelRoot model, string attributeName)
        => model.Types!.Single().Attributes!.Single(a => a.AttributeName == attributeName);
}
