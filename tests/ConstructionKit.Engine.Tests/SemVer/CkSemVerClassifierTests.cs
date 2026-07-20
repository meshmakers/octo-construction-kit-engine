using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     One positive test per rule-table row (docs/ck-semver-rules.md), driven end-to-end through
///     diff + classification of minimal fixture model pairs.
/// </summary>
public class CkSemVerClassifierTests
{
    private readonly CkModelDiffService _diffService = new();
    private readonly CkSemVerClassifier _classifier = new();

    private CkSemVerLevel ClassifyHighest(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        var changes = _diffService.Diff(baseline, current);
        Assert.NotEmpty(changes);
        var classified = _classifier.Classify(changes, baseline, current);
        return _classifier.GetRequiredLevel(classified);
    }

    // ── Major rules ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypeRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Types!.Clear();
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AttributeRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Attributes!.RemoveAll(a => a.AttributeId.Name == "WithDefault");
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void EnumRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Enums!.Clear();
        // The StateAttr attribute still references the enum, but the diff/classifier layer
        // does not resolve that — removal alone is the breaking change here.
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void RecordRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Records!.Clear();
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AssociationRoleRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.AssociationRoles!.Clear();
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void TypeAttributeRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.RemoveAll(a => a.AttributeName == "SerialNumber");
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AttributeValueTypeChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "SerialNumber").ValueType = AttributeValueTypesDto.Int;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AttributeValueCkEnumIdChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").ValueCkEnumId = $"{SemVerTestModels.ModelName}/Other";
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AttributeValueCkRecordIdChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "SerialNumber").ValueCkRecordId = $"{SemVerTestModels.ModelName}/Address";
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void TypeAttributeOptionalToRequired_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Single(a => a.AttributeName == "State").IsOptional = false;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewRequiredAttributeWithoutDefaultValues_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Add(new CkTypeAttributeDto
        {
            CkAttributeId = $"{SemVerTestModels.ModelName}/SerialNumber", AttributeName = "Extra", IsOptional = false
        });
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewRequiredAttributeReferencingForeignModel_IsMajorDefensively()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Add(new CkTypeAttributeDto
        {
            CkAttributeId = "Base/ForeignAttribute", AttributeName = "Foreign", IsOptional = false
        });
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void DerivedFromCkTypeIdChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).DerivedFromCkTypeId = "Base/OtherEntity";
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void DerivedFromCkRecordIdChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetRecord(current).DerivedFromCkRecordId = "Base/BaseRecord";
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Theory]
    [InlineData(MultiplicitiesDto.N, MultiplicitiesDto.ZeroOrOne)]
    [InlineData(MultiplicitiesDto.N, MultiplicitiesDto.One)]
    [InlineData(MultiplicitiesDto.ZeroOrOne, MultiplicitiesDto.One)]
    public void MultiplicityTightened_IsMajor(MultiplicitiesDto from, MultiplicitiesDto to)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetRole(baseline).InboundMultiplicity = from;
        SemVerTestModels.GetRole(current).InboundMultiplicity = to;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AssociationRoleNameChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetRole(current).InboundName = "Kids";
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void EnumValueRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        var stateEnum = SemVerTestModels.GetEnum(current);
        stateEnum.Values = stateEnum.Values.Where(v => v.Name != "On").ToList();
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void EnumValueKeyChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetEnum(current).Values.Single(v => v.Name == "On").Key = 7;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void EnumUseFlagsChanged_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetEnum(current).UseFlags = true;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void EnumNoLongerExtensible_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetEnum(baseline).IsExtensible = true;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Theory]
    [InlineData("isAbstract")]
    [InlineData("isFinal")]
    public void TypeBecomesAbstractOrFinal_IsMajor(string property)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        var machine = SemVerTestModels.GetMachine(current);
        if (property == "isAbstract")
        {
            machine.IsAbstract = true;
        }
        else
        {
            machine.IsFinal = true;
        }

        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Theory]
    [InlineData(IndexTypeDto.Unique)]
    [InlineData(IndexTypeDto.UniqueNotDeleted)]
    public void UniqueIndexAdded_IsMajor(IndexTypeDto indexType)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = indexType, Fields = [new CkIndexFieldsDto { AttributePaths = ["state"] }]
        });
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void TypeAssociationRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Associations!.Clear();
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void TypeNoLongerCollectionRoot_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(baseline).IsCollectionRoot = true;
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void DependencyRemoved_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Dependencies!.Clear();
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void DependencyMajorSwitch_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Dependencies = [new CkModelId("Base", "2.0.0")];
        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewMandatoryTypeAssociation_MultiplicityOne_IsMajor(bool outbound)
    {
        // Wiring a type to a role with multiplicity One imposes a new mandatory constraint on
        // entity creation — the association-level equivalent of a new required attribute.
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        foreach (var model in new[] { baseline, current })
        {
            model.AssociationRoles!.Add(new CkAssociationRoleDto
            {
                AssociationRoleId = "Ownership", InboundName = "Owned", OutboundName = "Owner",
                InboundMultiplicity = outbound ? MultiplicitiesDto.N : MultiplicitiesDto.One,
                OutboundMultiplicity = outbound ? MultiplicitiesDto.One : MultiplicitiesDto.N
            });
        }

        SemVerTestModels.GetMachine(current).Associations!.Add(new CkTypeAssociationDto
        {
            CkRoleId = $"{SemVerTestModels.ModelName}/Ownership", TargetCkTypeId = "Base/Entity"
        });

        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewTypeAssociationWithForeignRole_IsMajorDefensively()
    {
        // The multiplicity of a role defined in another model cannot be inspected here
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Associations!.Add(new CkTypeAssociationDto
        {
            CkRoleId = "Base/Related", TargetCkTypeId = "Base/Entity"
        });

        Assert.Equal(CkSemVerLevel.Major, ClassifyHighest(baseline, current));
    }

    // ── Minor rules ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NewType_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Types!.Add(new CkCompiledTypeDto { TypeId = "NewType", DerivedFromCkTypeId = "Base/Entity" });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewOptionalTypeAttribute_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Add(new CkTypeAttributeDto
        {
            CkAttributeId = $"{SemVerTestModels.ModelName}/SerialNumber", AttributeName = "Extra", IsOptional = true
        });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewRequiredAttributeWithDefaultValues_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Add(new CkTypeAttributeDto
        {
            CkAttributeId = $"{SemVerTestModels.ModelName}/WithDefault", AttributeName = "Count", IsOptional = false
        });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewEnumValue_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetEnum(current).Values.Add(new CkEnumValueDto { Key = 2, Name = "Standby" });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void RequiredToOptional_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Single(a => a.AttributeName == "SerialNumber").IsOptional = true;
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Theory]
    [InlineData(MultiplicitiesDto.One, MultiplicitiesDto.ZeroOrOne)]
    [InlineData(MultiplicitiesDto.One, MultiplicitiesDto.N)]
    [InlineData(MultiplicitiesDto.ZeroOrOne, MultiplicitiesDto.N)]
    public void MultiplicityRelaxed_IsMinor(MultiplicitiesDto from, MultiplicitiesDto to)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetRole(baseline).OutboundMultiplicity = from;
        SemVerTestModels.GetRole(current).OutboundMultiplicity = to;
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Theory]
    [InlineData("isAbstract")]
    [InlineData("isFinal")]
    public void TypeNoLongerAbstractOrFinal_IsMinor(string property)
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        var machine = SemVerTestModels.GetMachine(baseline);
        if (property == "isAbstract")
        {
            machine.IsAbstract = true;
        }
        else
        {
            machine.IsFinal = true;
        }

        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NonUniqueIndexAdded_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = IndexTypeDto.Text, Language = "en",
            Fields = [new CkIndexFieldsDto { Weight = 5, AttributePaths = ["serialNumber"] }]
        });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void IndexRemoved_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Indexes!.Clear();
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void DefaultValuesChanged_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "WithDefault").DefaultValues = [43];
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AutoCompleteValuesChanged_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Attributes!.Single(a => a.AttributeName == "State")
            .AutoCompleteValues = ["On", "Off"];
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void IsRuntimeStateChanged_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "StateAttr").IsRuntimeState = true;
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void AttributeMetaDataChanged_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "SerialNumber").MetaData =
            [new CkAttributeMetaDataDto { Key = "unit", Value = "mm" }];
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void EnumBecomesExtensible_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetEnum(current).IsExtensible = true;
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void ChangeStreamImagesToggled_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).EnableChangeStreamPreAndPostImages = true;
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void TypeBecomesCollectionRoot_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).IsCollectionRoot = true;
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewTypeAssociation_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetMachine(current).Associations!.Add(new CkTypeAssociationDto
        {
            CkRoleId = $"{SemVerTestModels.ModelName}/Parent", TargetCkTypeId = "Base/Entity"
        });
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void NewDependency_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Dependencies!.Add(new CkModelId("Extra", "1.0.0"));
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    [Fact]
    public void DependencyVersionWidenedWithoutMajorSwitch_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Dependencies = [new CkModelId("Base", "1.3.0")];
        Assert.Equal(CkSemVerLevel.Minor, ClassifyHighest(baseline, current));
    }

    // ── Patch rules ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescriptionChanges_ArePatch()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Description = "New model description";
        SemVerTestModels.GetMachine(current).Description = "New type description";
        SemVerTestModels.GetAttribute(current, "SerialNumber").Description = "New attribute description";
        SemVerTestModels.GetEnum(current).Description = "New enum description";

        var changes = _diffService.Diff(baseline, current);
        var classified = _classifier.Classify(changes, baseline, current);

        Assert.Equal(4, classified.Count);
        Assert.All(classified, c => Assert.Equal(CkSemVerLevel.Patch, c.Level));
        Assert.Equal(CkSemVerLevel.Patch, _classifier.GetRequiredLevel(classified));
    }

    // ── Aggregation and defensive default ───────────────────────────────────────────────

    [Fact]
    public void GetRequiredLevel_EmptyDiff_IsNone()
    {
        Assert.Equal(CkSemVerLevel.None, _classifier.GetRequiredLevel([]));
    }

    [Fact]
    public void GetRequiredLevel_ReturnsHighestLevel()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Description = "New description";                       // patch
        SemVerTestModels.GetEnum(current).Values.Add(new CkEnumValueDto { Key = 2, Name = "Standby" }); // minor
        current.Types!.Clear();                                        // major

        var changes = _diffService.Diff(baseline, current);
        var classified = _classifier.Classify(changes, baseline, current);

        Assert.Equal(CkSemVerLevel.Major, _classifier.GetRequiredLevel(classified));
    }

    [Fact]
    public void UnknownChange_IsClassifiedMajorDefensively()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        var unknownChange = new CkModelChange
        {
            ChangeKind = CkModelChangeKind.Modified, ElementKind = CkModelElementKind.Type,
            ElementId = "Machine-1", Property = "someFutureProperty", OldValue = "a", NewValue = "b"
        };

        var classified = _classifier.Classify([unknownChange], baseline, current);

        var single = Assert.Single(classified);
        Assert.Equal(CkSemVerLevel.Major, single.Level);
        Assert.Contains("defensively", single.Reason);
    }

    [Fact]
    public void Classification_CarriesReasonForEveryChange()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        current.Types!.Clear();
        current.Enums!.Clear();
        current.Description = "x";

        var changes = _diffService.Diff(baseline, current);
        var classified = _classifier.Classify(changes, baseline, current);

        Assert.All(classified, c => Assert.False(string.IsNullOrWhiteSpace(c.Reason)));
    }
}
