using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

public class CkModelDiffServiceTests
{
    private readonly CkModelDiffService _diffService = new();

    [Fact]
    public void Diff_IdenticalModels_ReturnsEmptyDiff()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();

        var changes = _diffService.Diff(baseline, current);

        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_IdenticalModelsWithBumpedVersion_ReturnsEmptyDiff()
    {
        // Self references carry the model version — a version bump alone must not
        // produce reference changes.
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel("1.1.0");

        var changes = _diffService.Diff(baseline, current);

        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_IsOrderInvariant()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Attributes!.Reverse();
        current.Enums!.Reverse();
        SemVerTestModels.GetMachine(current).Attributes!.Reverse();

        var changes = _diffService.Diff(baseline, current);

        Assert.Empty(changes);
    }

    [Theory]
    [InlineData(CkModelElementKind.Type)]
    [InlineData(CkModelElementKind.Attribute)]
    [InlineData(CkModelElementKind.Enum)]
    [InlineData(CkModelElementKind.Record)]
    [InlineData(CkModelElementKind.AssociationRole)]
    public void Diff_RemovedElement_ReportsRemovedChange(CkModelElementKind elementKind)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        switch (elementKind)
        {
            case CkModelElementKind.Type:
                current.Types!.Clear();
                break;
            case CkModelElementKind.Attribute:
                current.Attributes!.RemoveAll(a => a.AttributeId.Name == "WithDefault");
                break;
            case CkModelElementKind.Enum:
                current.Enums!.Clear();
                break;
            case CkModelElementKind.Record:
                current.Records!.Clear();
                break;
            case CkModelElementKind.AssociationRole:
                current.AssociationRoles!.Clear();
                break;
        }

        var changes = _diffService.Diff(baseline, current);

        Assert.Contains(changes, c => c.ElementKind == elementKind && c.ChangeKind == CkModelChangeKind.Removed);
    }

    [Theory]
    [InlineData(CkModelElementKind.Type)]
    [InlineData(CkModelElementKind.Attribute)]
    [InlineData(CkModelElementKind.Enum)]
    [InlineData(CkModelElementKind.Record)]
    [InlineData(CkModelElementKind.AssociationRole)]
    public void Diff_AddedElement_ReportsAddedChange(CkModelElementKind elementKind)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        switch (elementKind)
        {
            case CkModelElementKind.Type:
                current.Types!.Add(new CkCompiledTypeDto { TypeId = "NewType", DerivedFromCkTypeId = "Base/Entity" });
                break;
            case CkModelElementKind.Attribute:
                current.Attributes!.Add(new CkAttributeDto
                    { AttributeId = "NewAttribute", ValueType = AttributeValueTypesDto.String });
                break;
            case CkModelElementKind.Enum:
                current.Enums!.Add(new CkEnumDto { EnumId = "NewEnum" });
                break;
            case CkModelElementKind.Record:
                current.Records!.Add(new CkRecordDto { RecordId = "NewRecord" });
                break;
            case CkModelElementKind.AssociationRole:
                current.AssociationRoles!.Add(new CkAssociationRoleDto
                {
                    AssociationRoleId = "NewRole", InboundName = "In", OutboundName = "Out",
                    InboundMultiplicity = MultiplicitiesDto.N, OutboundMultiplicity = MultiplicitiesDto.N
                });
                break;
        }

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(elementKind, change.ElementKind);
        Assert.Equal(CkModelChangeKind.Added, change.ChangeKind);
    }

    [Fact]
    public void Diff_ChangedTypeProperty_ReportsModifiedChangeWithOldAndNewValue()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).IsFinal = true;

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.Type, change.ElementKind);
        Assert.Equal(CkModelChangeKind.Modified, change.ChangeKind);
        Assert.Equal("isFinal", change.Property);
        Assert.Equal("false", change.OldValue);
        Assert.Equal("true", change.NewValue);
    }

    [Fact]
    public void Diff_ChangedAttributeValueType_ReportsModifiedChange()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "SerialNumber").ValueType = AttributeValueTypesDto.Int;

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.Attribute, change.ElementKind);
        Assert.Equal("valueType", change.Property);
        Assert.Equal("String", change.OldValue);
    }

    [Fact]
    public void Diff_ChangedEnumValueKey_ReportsEnumValueModification()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetEnum(current).Values.Single(v => v.Name == "On").Key = 7;

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.EnumValue, change.ElementKind);
        Assert.Equal("key", change.Property);
        Assert.Equal("1", change.OldValue);
        Assert.Equal("7", change.NewValue);
        Assert.Equal("State-1/On", change.ElementId);
    }

    [Fact]
    public void Diff_AddedAndRemovedTypeAttribute_ReportsBoth()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        var machine = SemVerTestModels.GetMachine(current);
        machine.Attributes!.RemoveAll(a => a.AttributeName == "SerialNumber");
        machine.Attributes!.Add(new CkTypeAttributeDto
        {
            CkAttributeId = $"{SemVerTestModels.ModelName}/WithDefault", AttributeName = "Count", IsOptional = true
        });

        var changes = _diffService.Diff(baseline, current);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c is
        {
            ElementKind: CkModelElementKind.TypeAttribute, ChangeKind: CkModelChangeKind.Removed,
            ElementId: "Machine-1/SerialNumber"
        });
        Assert.Contains(changes, c => c is
        {
            ElementKind: CkModelElementKind.TypeAttribute, ChangeKind: CkModelChangeKind.Added,
            ElementId: "Machine-1/Count"
        });
    }

    [Fact]
    public void Diff_DependencyVersionChange_ReportsDependencyModification()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Dependencies = [new CkModelId("Base", "1.3.0")];

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.Dependency, change.ElementKind);
        Assert.Equal("version", change.Property);
        Assert.Equal("1.2.3", change.OldValue);
        Assert.Equal("1.3.0", change.NewValue);
    }

    [Fact]
    public void Diff_ForeignReferenceMinorVersionChange_ProducesNoReferenceNoise()
    {
        // Foreign references are compared by name + major version: a minor bump of the
        // dependency must only surface in the dependency diff, not on every reference.
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Dependencies = [new CkModelId("Base", "1.3.0")];
        SemVerTestModels.GetMachine(current).DerivedFromCkTypeId = "Base-1.3.0/Entity";

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.Dependency, change.ElementKind);
    }

    [Fact]
    public void Diff_ForeignReferenceMajorVersionChange_ReportsReferenceChange()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).DerivedFromCkTypeId = "Base-2.0.0/Entity";

        var changes = _diffService.Diff(baseline, current);

        Assert.Contains(changes, c => c is
        {
            ElementKind: CkModelElementKind.Type, Property: "derivedFromCkTypeId",
            OldValue: "Base/Entity-1", NewValue: "Base-2/Entity-1"
        });
    }

    [Fact]
    public void Diff_EquivalentScalarsOfDifferentClrTypes_CompareEqual()
    {
        // Baseline models come from the catalog JSON, current models from YAML — the boxed
        // CLR type of scalars may differ while the value is identical.
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "WithDefault").DefaultValues = [(short)42];
        SemVerTestModels.GetAttribute(current, "WithDefault").DefaultValues = [42L];

        var changes = _diffService.Diff(baseline, current);

        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_AddedUniqueIndex_ReportsIndexAddition()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = IndexTypeDto.Unique, Fields = [new CkIndexFieldsDto { AttributePaths = ["state"] }]
        });

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.TypeIndex, change.ElementKind);
        Assert.Equal(CkModelChangeKind.Added, change.ChangeKind);
        Assert.StartsWith("Unique on state", change.NewValue);
    }

    [Fact]
    public void Diff_ModelDescriptionChange_ReportsModelModification()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Description = "Another description";

        var changes = _diffService.Diff(baseline, current);

        var change = Assert.Single(changes);
        Assert.Equal(CkModelElementKind.Model, change.ElementKind);
        Assert.Equal("description", change.Property);
    }

    [Fact]
    public void Diff_TypeAssociationAddedAndRemoved_ReportsBoth()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        var machine = SemVerTestModels.GetMachine(current);
        machine.Associations!.Clear();
        machine.Associations.Add(new CkTypeAssociationDto
        {
            CkRoleId = $"{SemVerTestModels.ModelName}/Parent", TargetCkTypeId = "Base/Entity"
        });

        var changes = _diffService.Diff(baseline, current);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.ElementKind == CkModelElementKind.TypeAssociation &&
                                      c.ChangeKind == CkModelChangeKind.Removed);
        Assert.Contains(changes, c => c.ElementKind == CkModelElementKind.TypeAssociation &&
                                      c.ChangeKind == CkModelChangeKind.Added);
    }
}
