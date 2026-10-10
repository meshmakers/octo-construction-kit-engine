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

    public static TheoryData<string> I12ContractChanges =>
    [
        "optional parameter added", "required parameter added", "parameter removed", "parameter type changed",
        "parameter required to optional", "parameter optional to required", "sensitive changed", "result changed",
        "kind changed", "error added", "error removed"
    ];

    [Theory]
    [MemberData(nameof(I12ContractChanges))]
    public void I12_InterfaceMethodInvocationContractChanged_IsMajor(string change)
    {
        // AB#6336: a type in another model that redeclares the interface method with the previous contract breaks
        // with error 122 — so every contract change is Major on an interface method, also the ones that are Minor on
        // a type method (M2, M5 relaxed, M13).
        var current = Model();
        var method = Method(current, "interface");
        switch (change)
        {
            case "optional parameter added":
                method.Parameters!.Add(new CkMethodParameterDto { Name = "note", ValueType = AttributeValueTypesDto.String, IsOptional = true });
                break;
            case "required parameter added":
                method.Parameters!.Add(new CkMethodParameterDto { Name = "note", ValueType = AttributeValueTypesDto.String });
                break;
            case "parameter removed":
                method.Parameters!.RemoveAt(0);
                break;
            case "parameter type changed":
                method.Parameters![2].ValueCkRecordId = null;
                method.Parameters![2].ValueType = AttributeValueTypesDto.String;
                break;
            case "parameter required to optional":
                method.Parameters![1].IsOptional = true;
                break;
            case "parameter optional to required":
                method.Parameters![0].IsOptional = false;
                break;
            case "sensitive changed":
                method.Parameters![0].Sensitive = false;
                break;
            case "result changed":
                method.Result = null;
                break;
            case "kind changed":
                method.Kind = CkMethodKindDto.Static;
                method.Authorization!.AllowSelf = false;
                break;
            case "error added":
                method.Errors!.Add(new CkMethodErrorDto { Code = "LOCKED" });
                break;
            default:
                method.Errors!.RemoveAt(0);
                break;
        }

        var classified = Classify(Model(), current).Where(c => c.Level > CkSemVerLevel.None).ToList();
        Assert.Contains(classified, c => c.Level == CkSemVerLevel.Major && (c.Reason.Contains("(row I12)") ||
                                                                            c.Reason.Contains("row M")));
        Assert.Equal(CkSemVerLevel.Major, Level(Model(), current));
        if (change is "optional parameter added" or "parameter required to optional" or "sensitive changed")
        {
            // The three that the M rows rate Minor on a type method are lifted by I12.
            Assert.Contains(classified, c => c.Reason.Contains("(row I12)"));
        }
    }

    [Fact]
    public void I12_InterfaceMethodMetadata_KeepsItsMethodRowLevel()
    {
        // Gate cases X4 (timeout), X6 (role added), X7 (idempotent false -> true), description: not part of the
        // contract, so redeclaring implementors keep compiling and the M-row level applies.
        var current = Model();
        var method = Method(current, "interface");
        method.Execution = new CkMethodExecutionDto { TimeoutSeconds = 45, Idempotent = true };
        method.Authorization!.Roles = [.. method.Authorization.Roles ?? [], "Ops"];
        method.Description = "other";

        var classified = Classify(Model(), current);
        Assert.DoesNotContain(classified, c => c.Reason.Contains("(row I12)"));
        Assert.True(Level(Model(), current) < CkSemVerLevel.Major);

        // The same optional parameter on a TYPE method stays Minor (M2).
        current = Model();
        Method(current, "type").Parameters!.Add(new CkMethodParameterDto
        {
            Name = "note", ValueType = AttributeValueTypesDto.String, IsOptional = true
        });
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
    }
}
