using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// Unit tests for <see cref="BlueprintService.PreserveAttributesForEntity"/>. The static
/// method drives the per-entity preservation loop and is the testable seam of the
/// broader runtime-state preservation feature — it carries the full decision logic
/// (which seed attrs to rewrite, which to leave alone) without any repository or cache
/// dependencies.
/// </summary>
public class BlueprintServicePreserveAttributesForEntityTests
{
    // The CK side carries a versioned model id ("Test-1.0.0"), while the RT side (seed
    // entities deserialised from YAML) carries only the bare model name ("Test"). The
    // cross-type CkId.Equals(RtCkId) overload compares `CkId.ModelId.Name` against
    // `RtCkId.ModelId` (a string), so these two views must align name-wise but not
    // version-wise — same contract the production YAML deserialiser produces.
    private const string TestCkModelId = "Test-1.0.0";
    private const string TestRtModelId = "Test";

    [Fact]
    public void FlaggedAttrPresentOnBoth_PreservesExistingValue()
    {
        // Scenario: blueprint bump with a `DeploymentState=0` seed, but the tenant
        // already has the entity with DeploymentState=2 (Deployed). Without this
        // preservation step the next blueprint re-apply would reset the live entity
        // back to Undeployed — the exact regression the feature exists to prevent.
        var flagged = new[]
        {
            BuildTypeAttr("DeploymentState", isRuntimeState: true),
        };
        var seed = SeedEntity(("DeploymentState", 0));
        var existing = ExistingEntity(("DeploymentState", 2));

        var preserved = BlueprintService.PreserveAttributesForEntity(seed, existing, flagged);

        Assert.Equal(1, preserved);
        Assert.Equal(2, seed.Attributes.Single(a => a.Id.ElementId.Name == "DeploymentState").Value);
    }

    [Fact]
    public void UnflaggedAttrPresentOnBoth_LeavesSeedValueUntouched()
    {
        // Hostname is blueprint-managed (NOT runtime-state) — the seed value must win
        // on re-apply even when the existing entity has a different value. Otherwise
        // blueprint authors couldn't ever update non-runtime fields.
        // No flagged attrs at all → preservation is a no-op regardless of seed/existing.
        var flagged = Array.Empty<CkTypeAttributeGraph>();
        var seed = SeedEntity(("Hostname", "adapter.new"));
        var existing = ExistingEntity(("Hostname", "adapter.old"));

        var preserved = BlueprintService.PreserveAttributesForEntity(seed, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.Equal("adapter.new", seed.Attributes.Single(a => a.Id.ElementId.Name == "Hostname").Value);
    }

    [Fact]
    public void MixedFlaggedAndUnflagged_OnlyFlaggedPreserved()
    {
        // Realistic case for an Adapter entity: DeploymentState is runtime-state,
        // Hostname is seed-managed. Preservation must rewrite the former and leave
        // the latter for the blueprint author to drive.
        var flagged = new[]
        {
            BuildTypeAttr("DeploymentState", isRuntimeState: true),
        };
        var seed = SeedEntity(
            ("DeploymentState", 0),
            ("Hostname", "adapter.new"));
        var existing = ExistingEntity(
            ("DeploymentState", 2),
            ("Hostname", "adapter.old"));

        var preserved = BlueprintService.PreserveAttributesForEntity(seed, existing, flagged);

        Assert.Equal(1, preserved);
        Assert.Equal(2, seed.Attributes.Single(a => a.Id.ElementId.Name == "DeploymentState").Value);
        Assert.Equal("adapter.new", seed.Attributes.Single(a => a.Id.ElementId.Name == "Hostname").Value);
    }

    [Fact]
    public void FlaggedAttrMissingFromExisting_LeavesSeedValueUntouched()
    {
        // The blueprint added a brand-new runtime-state attribute (e.g. a CK bump
        // introducing LastSyncedSequenceNumber). The pre-existing entity has never
        // carried this attribute, so there is nothing to preserve — the seed default
        // is what the entity will have on first read post-import.
        var flagged = new[]
        {
            BuildTypeAttr("LastSyncedSequenceNumber", isRuntimeState: true),
        };
        var seed = SeedEntity(("LastSyncedSequenceNumber", 0));
        var existing = ExistingEntity(); // no attributes

        var preserved = BlueprintService.PreserveAttributesForEntity(seed, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.Equal(0, seed.Attributes.Single(a => a.Id.ElementId.Name == "LastSyncedSequenceNumber").Value);
    }

    [Fact]
    public void FlaggedAttrMissingFromSeed_NoOp()
    {
        // The CK type defines a runtime-state attribute (e.g. LastDeploymentError),
        // but the blueprint seed deliberately omits it because there's no sensible
        // default. We should NOT touch the seed in this case — there's nothing to
        // rewrite, and the entity keeps whatever value it had before (via the
        // separate value on the CK default of the attribute).
        var flagged = new[]
        {
            BuildTypeAttr("LastDeploymentError", isRuntimeState: true),
        };
        var seed = SeedEntity(("Hostname", "adapter.new"));
        var existing = ExistingEntity(
            ("LastDeploymentError", "previous failure"),
            ("Hostname", "adapter.old"));

        var preserved = BlueprintService.PreserveAttributesForEntity(seed, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.DoesNotContain(seed.Attributes, a => a.Id.ElementId.Name == "LastDeploymentError");
    }

    [Fact]
    public void MultipleFlaggedAttrs_AllPreservedIndependently()
    {
        // The complete System.Communication/Adapter set on re-apply: deployment
        // status, communication status, configuration status, and sync counter all
        // present on both sides. Each must be preserved independently — partial
        // preservation would leave the entity in an internally-inconsistent state.
        var flagged = new[]
        {
            BuildTypeAttr("DeploymentState", isRuntimeState: true),
            BuildTypeAttr("CommunicationState", isRuntimeState: true),
            BuildTypeAttr("ConfigurationState", isRuntimeState: true),
            BuildTypeAttr("LastSyncedSequenceNumber", isRuntimeState: true),
        };
        var seed = SeedEntity(
            ("DeploymentState", 0),
            ("CommunicationState", 0),
            ("ConfigurationState", 0),
            ("LastSyncedSequenceNumber", 0));
        var existing = ExistingEntity(
            ("DeploymentState", 2),
            ("CommunicationState", 1),
            ("ConfigurationState", 2),
            ("LastSyncedSequenceNumber", 47));

        var preserved = BlueprintService.PreserveAttributesForEntity(seed, existing, flagged);

        Assert.Equal(4, preserved);
        Assert.Equal(2, seed.Attributes.Single(a => a.Id.ElementId.Name == "DeploymentState").Value);
        Assert.Equal(1, seed.Attributes.Single(a => a.Id.ElementId.Name == "CommunicationState").Value);
        Assert.Equal(2, seed.Attributes.Single(a => a.Id.ElementId.Name == "ConfigurationState").Value);
        Assert.Equal(47, seed.Attributes.Single(a => a.Id.ElementId.Name == "LastSyncedSequenceNumber").Value);
    }

    private static CkTypeAttributeGraph BuildTypeAttr(string name, bool isRuntimeState)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var ckAttrDto = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = AttributeValueTypesDto.String,
            IsRuntimeState = isRuntimeState,
        };
        var ckAttrGraph = new CkAttributeGraph(attrId, ckAttrDto);
        var ckTypeAttrDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId,
            AttributeName = name,
        };
        return new CkTypeAttributeGraph(attrId, ckTypeAttrDto, ckAttrGraph);
    }

    private static RtEntityTcDto SeedEntity(params (string Name, object Value)[] attrs)
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

    private static RtEntity ExistingEntity(params (string Name, object Value)[] attrs)
    {
        // RtEntity is the runtime form; attribute storage is a Dictionary<string,object?>
        // keyed by the attribute *name* (PascalCase), not the CK id. That matches what
        // RtEntity.GetAttributeValue uses; the preservation code looks values up the
        // same way to stay consistent with the rest of the runtime engine.
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
