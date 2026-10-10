using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6267 rows I1–I11: an interface grows additively within its element version.
/// </summary>
public class InterfaceRowTests
{
    [Fact]
    public void I1_OptionalInterfaceAttributeAdded_IsMinor()
    {
        var current = Model();
        Serialized(current).Attributes.Add(new CkInterfaceAttributeDto { CkAttributeId = $"{M}/WithDefault", AttributeName = "Extra", IsOptional = true });

        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.Contains("Serialized-1", change.Reason);
    }

    [Fact]
    public void I2_OptionalInterfaceAssociationAdded_IsMinor()
    {
        var current = Model();
        Serialized(current).Associations!.Add(new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owner", TargetCkTypeId = $"{M}/Machine", IsOptional = true });

        Assert.Equal(CkSemVerLevel.Minor, Assert.Single(Classify(Model(), current)).Level);
    }

    [Fact]
    public void I3_RequiredAttributeOrAssociationAdded_IsMajor_AndRecommendsANewInterfaceVersion()
    {
        var current = Model();
        Serialized(current).Attributes.Add(new CkInterfaceAttributeDto { CkAttributeId = $"{M}/WithDefault", AttributeName = "Extra" });
        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.Contains("new interface version", change.Reason);

        current = Model();
        Serialized(current).Associations!.Add(new CkInterfaceAssociationDto { CkRoleId = $"{M}/Owner", TargetCkTypeId = $"{M}/Machine" });
        Assert.Equal(CkSemVerLevel.Major, Assert.Single(Classify(Model(), current)).Level);
    }

    [Fact]
    public void I4_MemberRemovedOrRenamed_IsMajor()
    {
        var current = Model();
        Serialized(current).Attributes.RemoveAt(1);
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));

        current = Model();
        Serialized(current).Attributes[1].AttributeName = "Status";
        var classified = Classify(Model(), current);
        Assert.Equal(2, classified.Count);
        Assert.All(classified, c => Assert.Contains(c.Level, new[] { CkSemVerLevel.Minor, CkSemVerLevel.Major }));
        Assert.Contains(classified, c => c.Change.ChangeKind == CkModelChangeKind.Removed && c.Level == CkSemVerLevel.Major);

        current = Model();
        Serialized(current).Associations = null;
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));
    }

    [Fact]
    public void I5_MemberAttributeIdChanged_IsMajor()
    {
        var current = Model();
        Serialized(current).Attributes[0].CkAttributeId = $"{M}/WithDefault";

        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal("id", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
    }

    [Fact]
    public void I6_AssociationMultiplicityOrTargetChanged_IsMajor()
    {
        var current = Model();
        Serialized(current).Associations![0].Multiplicity = MultiplicitiesDto.N;
        Assert.Equal(CkSemVerLevel.Major, Assert.Single(Classify(Model(), current)).Level);

        current = Model();
        Serialized(current).Associations![0].TargetCkTypeId = "Base/Entity";
        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkModelChangeKind.Modified, change.Change.ChangeKind);
        Assert.Equal("target", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
    }

    [Fact]
    public void I7_MemberMadeRequired_IsMajor_MadeOptional_IsMinor()
    {
        var current = Model();
        Serialized(current).Attributes[1].IsOptional = false;
        Assert.Equal(CkSemVerLevel.Major, Assert.Single(Classify(Model(), current)).Level);

        current = Model();
        Serialized(current).Attributes[0].IsOptional = true;
        Assert.Equal(CkSemVerLevel.Minor, Assert.Single(Classify(Model(), current)).Level);

        current = Model();
        Serialized(current).Associations![0].IsOptional = false;
        Assert.Equal(CkSemVerLevel.Major, Assert.Single(Classify(Model(), current)).Level);
    }

    [Fact]
    public void I8_ExtendsAddedOrRemoved_IsMajor()
    {
        var current = Model();
        current.Interfaces!.Add(new CkInterfaceDto { InterfaceId = "Base-1", Attributes = [] });
        var extended = Model();
        extended.Interfaces!.Add(new CkInterfaceDto { InterfaceId = "Base-1", Attributes = [] });
        Serialized(extended).Extends = [new CkId<CkInterfaceId>($"{M}/Base-1")];

        var added = Assert.Single(Classify(current, extended));
        Assert.Equal(CkModelElementKind.InterfaceExtends, added.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Major, added.Level);
        Assert.Equal(CkSemVerLevel.Major, Assert.Single(Classify(extended, current)).Level);
    }

    [Fact]
    public void I9_DeprecatedSetOrWithdrawn_IsMinor()
    {
        var current = Model();
        Serialized(current).Deprecated = true;

        Assert.Equal(CkSemVerLevel.Minor, Assert.Single(Classify(Model(), current)).Level);
        Assert.Equal(CkSemVerLevel.Minor, Assert.Single(Classify(current, Model())).Level);
    }

    [Fact]
    public void I10_InterfaceAdded_IsMinor_Removed_IsMajor()
    {
        var current = Model();
        current.Interfaces!.Add(new CkInterfaceDto { InterfaceId = "Labeled-1", Attributes = [] });

        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
        Assert.Equal(CkSemVerLevel.Major, Level(current, Model()));
    }

    [Fact]
    public void I11_MethodAddedToAnInterface_IsMajor()
    {
        var current = Model();
        Serialized(current).Methods!.Add(new CkMethodDto { MethodId = "Reset-1" });

        var change = Assert.Single(Classify(Model(), current));
        Assert.Equal(CkModelElementKind.InterfaceMethod, change.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Major, change.Level);
    }
}
