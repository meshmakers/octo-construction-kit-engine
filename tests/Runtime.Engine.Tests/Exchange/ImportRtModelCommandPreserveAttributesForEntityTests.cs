using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
/// Unit tests for <see cref="ImportRtModelCommand.PreserveAttributesForEntity"/>. The static
/// method drives the per-entity preservation loop and is the testable seam of the broader
/// runtime-state preservation feature — it carries the full decision logic (which incoming
/// attrs to rewrite, which to leave alone) without any repository or cache dependencies. It runs
/// on every Upsert import (blueprint seed apply, plain ImportRt, CK-model migration), so an
/// activated stream-data archive or a "Deployed" adapter survives a re-import (AB#4582 / AB#4589).
/// </summary>
public class ImportRtModelCommandPreserveAttributesForEntityTests
{
    // The CK side carries a versioned model id ("Test-1.0.0"), while the RT side (import
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
        var model = ModelEntity(("DeploymentState", 0));
        var existing = ExistingEntity(("DeploymentState", 2));

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(1, preserved);
        Assert.Equal(2, model.Attributes.Single(a => a.Id.ElementId.Name == "DeploymentState").Value);
    }

    [Fact]
    public void UnflaggedAttrPresentOnBoth_LeavesImportedValueUntouched()
    {
        // Hostname is blueprint-managed (NOT runtime-state) — the imported value must win
        // on re-apply even when the existing entity has a different value. Otherwise
        // blueprint authors couldn't ever update non-runtime fields.
        // No flagged attrs at all → preservation is a no-op regardless of model/existing.
        var flagged = Array.Empty<CkTypeAttributeGraph>();
        var model = ModelEntity(("Hostname", "adapter.new"));
        var existing = ExistingEntity(("Hostname", "adapter.old"));

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.Equal("adapter.new", model.Attributes.Single(a => a.Id.ElementId.Name == "Hostname").Value);
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
        var model = ModelEntity(
            ("DeploymentState", 0),
            ("Hostname", "adapter.new"));
        var existing = ExistingEntity(
            ("DeploymentState", 2),
            ("Hostname", "adapter.old"));

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(1, preserved);
        Assert.Equal(2, model.Attributes.Single(a => a.Id.ElementId.Name == "DeploymentState").Value);
        Assert.Equal("adapter.new", model.Attributes.Single(a => a.Id.ElementId.Name == "Hostname").Value);
    }

    [Fact]
    public void FlaggedAttrMissingFromExisting_LeavesImportedValueUntouched()
    {
        // The blueprint added a brand-new runtime-state attribute (e.g. a CK bump
        // introducing LastSyncedSequenceNumber). The pre-existing entity has never
        // carried this attribute, so there is nothing to preserve — the imported default
        // is what the entity will have on first read post-import.
        var flagged = new[]
        {
            BuildTypeAttr("LastSyncedSequenceNumber", isRuntimeState: true),
        };
        var model = ModelEntity(("LastSyncedSequenceNumber", 0));
        var existing = ExistingEntity(); // no attributes

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.Equal(0, model.Attributes.Single(a => a.Id.ElementId.Name == "LastSyncedSequenceNumber").Value);
    }

    [Fact]
    public void FlaggedAttrMissingFromModel_NoOp()
    {
        // The CK type defines a runtime-state attribute (e.g. LastDeploymentError),
        // but the import model deliberately omits it because there's no sensible
        // default. We should NOT touch the model in this case — there's nothing to
        // rewrite, and the entity keeps whatever value it had before (via the
        // separate value on the CK default of the attribute).
        var flagged = new[]
        {
            BuildTypeAttr("LastDeploymentError", isRuntimeState: true),
        };
        var model = ModelEntity(("Hostname", "adapter.new"));
        var existing = ExistingEntity(
            ("LastDeploymentError", "previous failure"),
            ("Hostname", "adapter.old"));

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.DoesNotContain(model.Attributes, a => a.Id.ElementId.Name == "LastDeploymentError");
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
        var model = ModelEntity(
            ("DeploymentState", 0),
            ("CommunicationState", 0),
            ("ConfigurationState", 0),
            ("LastSyncedSequenceNumber", 0));
        var existing = ExistingEntity(
            ("DeploymentState", 2),
            ("CommunicationState", 1),
            ("ConfigurationState", 2),
            ("LastSyncedSequenceNumber", 47));

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(4, preserved);
        Assert.Equal(2, model.Attributes.Single(a => a.Id.ElementId.Name == "DeploymentState").Value);
        Assert.Equal(1, model.Attributes.Single(a => a.Id.ElementId.Name == "CommunicationState").Value);
        Assert.Equal(2, model.Attributes.Single(a => a.Id.ElementId.Name == "ConfigurationState").Value);
        Assert.Equal(47, model.Attributes.Single(a => a.Id.ElementId.Name == "LastSyncedSequenceNumber").Value);
    }

    [Fact]
    public void ArchiveStatus_ActivatedArchive_SurvivesForcedReImport()
    {
        // AB#4582/AB#4589: EnergyCommunity.Base seeds every archive with Archive.Status=2
        // (Disabled) so a fresh install never requires CrateDB; ActivateArchive later
        // flips the live archive to 1 (Activated) and provisions the table. A forced
        // Upsert re-import — blueprint InstallBlueprint -f OR a plain ImportRt -r of the
        // voest archive YAML — would rewrite Status back to Disabled without the runtime-
        // state flag; the archive then reads as not activated and stream-data queries fail
        // with STREAMDATA_ARCHIVE_NOT_ACTIVATED even though the CrateDB rows are untouched.
        // With Archive.Status flagged isRuntimeState the live Activated value is preserved,
        // so the re-import is a no-op on the lifecycle status.
        var flagged = new[]
        {
            BuildTypeAttr("Status", isRuntimeState: true),
        };
        var model = ModelEntity(("Status", 2));     // imported Disabled
        var existing = ExistingEntity(("Status", 1)); // live Activated

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(1, preserved);
        Assert.Equal(1, model.Attributes.Single(a => a.Id.ElementId.Name == "Status").Value);
    }

    [Fact]
    public void ArchiveStatus_FreshImport_KeepsImportedDisabledValue()
    {
        // AB#4582/AB#4589: the flag must not change fresh-import behaviour. With no existing
        // archive entity there is nothing to preserve, so the imported Disabled (2) value
        // lands and the post-install ActivateArchive phase can activate it as before.
        var flagged = new[]
        {
            BuildTypeAttr("Status", isRuntimeState: true),
        };
        var model = ModelEntity(("Status", 2));
        var existing = ExistingEntity(); // fresh tenant, no archive yet

        var preserved = ImportRtModelCommand.PreserveAttributesForEntity(model, existing, flagged);

        Assert.Equal(0, preserved);
        Assert.Equal(2, model.Attributes.Single(a => a.Id.ElementId.Name == "Status").Value);
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

    private static RtEntityTcDto ModelEntity(params (string Name, object Value)[] attrs)
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
