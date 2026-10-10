using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6266 rows N1–N5: internal elements are not part of the compatibility surface.
/// </summary>
public class InternalElementRowTests
{
    /// <summary>
    ///     The row model without the public interface association member that targets Machine: with it, an internal
    ///     Machine would be reachable from the public interface Serialized-1 (129, AB#6334) and the classifier treats
    ///     it as public (defence in depth).
    /// </summary>
    private static CkCompiledModelRoot PublicModel()
    {
        var model = Model();
        Serialized(model).Associations![0].TargetCkTypeId = "Base/Entity";
        return model;
    }

    private static CkCompiledModelRoot WithInternalMachine()
    {
        var model = PublicModel();
        Machine(model).Visibility = CkVisibilityDto.Internal;
        return model;
    }

    [Fact]
    public void N1_InternalElementAddedRemovedOrModified_IsAtMostMinor()
    {
        // Modified: isFinal false -> true is Major for a public type
        var current = WithInternalMachine();
        Machine(current).IsFinal = true;
        var change = Assert.Single(Classify(WithInternalMachine(), current));
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.StartsWith(CkSemVerClassifier.InternalReason, change.Reason);

        // Removed: an internal enum that no public element references
        var baseline = Model();
        baseline.Enums!.Add(new CkEnumDto
        {
            EnumId = "Hidden", Visibility = CkVisibilityDto.Internal,
            Values = [new CkEnumValueDto { Key = 0, Name = "Off" }]
        });
        var withoutEnum = Model();
        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, withoutEnum));

        // Added: an internal record
        var withRecord = Model();
        withRecord.Records!.Add(new CkRecordDto { RecordId = "Hidden", Visibility = CkVisibilityDto.Internal });
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), withRecord));

        // Description-only stays Patch
        var described = WithInternalMachine();
        Machine(described).Description = "Other text";
        Assert.Equal(CkSemVerLevel.Patch, Level(WithInternalMachine(), described));
    }

    [Fact]
    public void N2_ChangeToAMemberOfAnInternalOwner_IsMinor()
    {
        // Required type attribute without defaults (Major for a public type)
        var current = WithInternalMachine();
        Machine(current).Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/SerialNumber", AttributeName = "Second" });
        Assert.Equal(CkSemVerLevel.Minor, Level(WithInternalMachine(), current));

        // Type association removed, unique index added
        current = WithInternalMachine();
        Machine(current).Associations = [];
        Machine(current).Indexes!.Add(new CkTypeIndexDto
        {
            IndexType = IndexTypeDto.Unique, Fields = [new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }]
        });
        Assert.Equal(CkSemVerLevel.Minor, Level(WithInternalMachine(), current));

        // Required parameter on an internal method of a public type
        var baseline = Model();
        Method(baseline, "type").Visibility = CkVisibilityDto.Internal;
        current = Model();
        Method(current, "type").Visibility = CkVisibilityDto.Internal;
        Method(current, "type").Parameters!.Add(new CkMethodParameterDto { Name = "reason", ValueType = AttributeValueTypesDto.String });
        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, current));

        // Required member of an internal interface (its implementor Machine is internal, too: a public implementor
        // would expose the interface, AB#6334)
        baseline = WithInternalMachine();
        Serialized(baseline).Visibility = CkVisibilityDto.Internal;
        current = WithInternalMachine();
        Serialized(current).Visibility = CkVisibilityDto.Internal;
        Serialized(current).Attributes.Add(new CkInterfaceAttributeDto { CkAttributeId = $"{M}/WithDefault", AttributeName = "Extra" });
        Assert.All(Classify(baseline, current), c => Assert.StartsWith(CkSemVerClassifier.InternalReason, c.Reason));
        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, current));
    }

    [Fact]
    public void N3_ElementPublicInTheBaselineRemoved_IsMajor()
    {
        var current = Model();
        current.Types = [];

        Assert.Contains(Classify(Model(), current),
            c => c.Change is { ElementKind: CkModelElementKind.Type, ChangeKind: CkModelChangeKind.Removed } &&
                 c.Level == CkSemVerLevel.Major);
    }

    [Fact]
    public void N4_VisibilityPublicToInternal_IsMajor_InternalToPublic_IsMinor()
    {
        var change = Assert.Single(Classify(PublicModel(), WithInternalMachine()));
        Assert.Equal("visibility", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Major, change.Level);

        change = Assert.Single(Classify(WithInternalMachine(), PublicModel()));
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }

    [Fact]
    public void N5_ElementMadePublicAndChangedInTheSameRelease_FollowsThePublicRules()
    {
        var current = PublicModel();
        Machine(current).IsFinal = true;
        Machine(current).Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/SerialNumber", AttributeName = "Second" });

        var classified = Classify(WithInternalMachine(), current);

        Assert.Contains(classified, c => c.Change.Property == "isFinal" && c.Level == CkSemVerLevel.Major);
        Assert.Contains(classified, c => c.Change.ElementKind == CkModelElementKind.TypeAttribute && c.Level == CkSemVerLevel.Major);
        Assert.DoesNotContain(classified, c => c.Reason.StartsWith(CkSemVerClassifier.InternalReason, StringComparison.Ordinal));
    }

    [Fact]
    public void N_InternalElementReachableFromAPublicOne_IsNotCapped()
    {
        // AB#6334 defence in depth: a model compiled by an older ckc may let a public element reference an internal
        // one (the compiler now rejects it with 129). The public attribute StateAttr, assigned by the public Machine,
        // uses the internal enum State: removing a value of it follows the public rule (E2, Major).
        static CkCompiledModelRoot Inconsistent()
        {
            var model = PublicModel();
            SemVerTestModels.GetEnum(model).Visibility = CkVisibilityDto.Internal;
            return model;
        }

        var current = Inconsistent();
        var enumDto = SemVerTestModels.GetEnum(current);
        enumDto.Values = enumDto.Values!.Take(1).ToList();

        var change = Assert.Single(Classify(Inconsistent(), current));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.DoesNotContain(CkSemVerClassifier.InternalReason, change.Reason);

        // An internal type reached from the public interface Serialized-1 (association member target) exposes its
        // members: removing an attribute is Major (T3), not the capped Minor.
        var baseline = Model();
        Machine(baseline).Visibility = CkVisibilityDto.Internal;
        current = Model();
        Machine(current).Visibility = CkVisibilityDto.Internal;
        Machine(current).Attributes!.RemoveAt(1);
        Assert.Equal(CkSemVerLevel.Major, Level(baseline, current));
    }

    [Fact]
    public void N_InternalMethodOfAPublicInterface_IsNotCapped()
    {
        // AB#6335 defence in depth: an internal method added to a public interface follows I11 (Major).
        var current = Model();
        Methods(current, "interface").Add(new CkMethodDto { MethodId = "Reset-1", Visibility = CkVisibilityDto.Internal });

        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.DoesNotContain(CkSemVerClassifier.InternalReason, change.Reason);
    }

    private static CkCompiledModelRoot WithInternalHelperPointingAtMachine(string inboundName = "OwnedBy")
    {
        var model = PublicModel();
        model.AssociationRoles!.Add(new CkAssociationRoleDto
        {
            AssociationRoleId = "Owns", Visibility = CkVisibilityDto.Internal, InboundName = inboundName,
            OutboundName = "Owns", InboundMultiplicity = MultiplicitiesDto.N, OutboundMultiplicity = MultiplicitiesDto.N
        });
        model.Types!.Add(new CkCompiledTypeDto
        {
            TypeId = "Helper", Visibility = CkVisibilityDto.Internal, DerivedFromCkTypeId = "Base/Entity",
            Associations = [new CkTypeAssociationDto { CkRoleId = $"{M}/Owns", TargetCkTypeId = $"{M}/Machine" }]
        });
        return model;
    }

    [Fact]
    public void N_InternalTypesAssociationToAPublicType_IsPublicSurface()
    {
        // N1 (platform-owner decision 2026-10-10): the association adds an inbound navigation on the public Machine
        // (GraphQL), inherited by derived types in other models. Removing it or renaming its role is Major.
        var removed = WithInternalHelperPointingAtMachine();
        removed.Types!.Single(t => t.TypeId.Name == "Helper").Associations = [];
        var change = Assert.Single(Classify(WithInternalHelperPointingAtMachine(), removed));
        Assert.Equal(CkModelElementKind.TypeAssociation, change.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.DoesNotContain(CkSemVerClassifier.InternalReason, change.Reason);

        var renamed = Assert.Single(Classify(WithInternalHelperPointingAtMachine(),
            WithInternalHelperPointingAtMachine("Owners")));
        Assert.Equal(CkModelElementKind.AssociationRole, renamed.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Major, renamed.Level);

        // An internal type's association to another INTERNAL type stays capped (row N2).
        static CkCompiledModelRoot InternalToInternal(bool withAssociation)
        {
            var model = WithInternalHelperPointingAtMachine();
            model.Types!.Add(new CkCompiledTypeDto { TypeId = "Other", Visibility = CkVisibilityDto.Internal, DerivedFromCkTypeId = "Base/Entity" });
            var helper = model.Types!.Single(t => t.TypeId.Name == "Helper");
            helper.Associations = withAssociation
                ? [new CkTypeAssociationDto { CkRoleId = $"{M}/Parent", TargetCkTypeId = $"{M}/Other" }]
                : [];
            return model;
        }

        var capped = Assert.Single(Classify(InternalToInternal(true), InternalToInternal(false)));
        Assert.Equal(CkSemVerLevel.Minor, capped.Level);
    }
}
