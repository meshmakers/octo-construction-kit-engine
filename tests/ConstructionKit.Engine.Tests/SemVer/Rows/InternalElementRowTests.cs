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
    private static CkCompiledModelRoot WithInternalMachine()
    {
        var model = Model();
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

        // Removed: an internal enum
        var baseline = Model();
        SemVerTestModels.GetEnum(baseline).Visibility = CkVisibilityDto.Internal;
        var withoutEnum = Model();
        withoutEnum.Enums = [];
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

        // Required member of an internal interface
        baseline = Model();
        Serialized(baseline).Visibility = CkVisibilityDto.Internal;
        current = Model();
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
        var change = Assert.Single(Classify(Model(), WithInternalMachine()));
        Assert.Equal("visibility", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Major, change.Level);

        change = Assert.Single(Classify(WithInternalMachine(), Model()));
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }

    [Fact]
    public void N5_ElementMadePublicAndChangedInTheSameRelease_FollowsThePublicRules()
    {
        var current = Model();
        Machine(current).IsFinal = true;
        Machine(current).Attributes!.Add(new CkTypeAttributeDto { CkAttributeId = $"{M}/SerialNumber", AttributeName = "Second" });

        var classified = Classify(WithInternalMachine(), current);

        Assert.Contains(classified, c => c.Change.Property == "isFinal" && c.Level == CkSemVerLevel.Major);
        Assert.Contains(classified, c => c.Change.ElementKind == CkModelElementKind.TypeAttribute && c.Level == CkSemVerLevel.Major);
        Assert.DoesNotContain(classified, c => c.Reason.StartsWith(CkSemVerClassifier.InternalReason, StringComparison.Ordinal));
    }
}
