using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6268 rows M1–M14: per-field verdict for method changes, for a type method and an interface method alike.
/// </summary>
public class MethodRowTests
{
    /// <summary>
    ///     Applies <paramref name="change" /> to the full method of <paramref name="owner" /> and returns the classified
    ///     changes; asserts that every method change also carries the readable signature summary (level None) when the
    ///     signature rendering changed.
    /// </summary>
    private static IReadOnlyList<CkClassifiedModelChange> Change(string owner, Action<CkMethodDto> change)
    {
        var current = Model();
        change(Method(current, owner));
        return Classify(Model(), current);
    }

    private static CkSemVerLevel Required(IReadOnlyList<CkClassifiedModelChange> classified) =>
        classified.Select(c => c.Level).DefaultIfEmpty(CkSemVerLevel.None).Max();

    [Theory]
    [InlineData("type", CkSemVerLevel.Minor)]
    [InlineData("interface", CkSemVerLevel.Major)] // row I11: an interface method cannot be added optionally yet
    public void M1_MethodAdded_Removed_IsMajor(string owner, CkSemVerLevel added)
    {
        var current = Model();
        Methods(current, owner).Add(new CkMethodDto { MethodId = "Calibrate-1" });

        Assert.Equal(added, Level(Model(), current));
        Assert.Equal(CkSemVerLevel.Major, Level(current, Model()));
    }

    [Theory]
    [InlineData("type", CkSemVerLevel.Minor)]
    [InlineData("interface", CkSemVerLevel.Major)] // row I12 (AB#6336): contract change on an interface method
    public void M2_OptionalParameterAdded_IsMinor(string owner, CkSemVerLevel expected)
    {
        var classified = Change(owner, m => m.Parameters!.Add(new CkMethodParameterDto
        {
            Name = "comment", ValueType = AttributeValueTypesDto.String, IsOptional = true
        }));

        Assert.Equal(expected, Required(classified));
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.MethodParameter, ChangeKind: CkModelChangeKind.Added });
        // The readable before/after signature is still reported, without deciding the level.
        Assert.Contains(classified, c => c.Change.Property == "signature" && c.Level == CkSemVerLevel.None);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M3_RequiredParameterAdded_ParameterRemovedOrRenamed_IsMajor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters!.Add(new CkMethodParameterDto
        {
            Name = "reason", ValueType = AttributeValueTypesDto.String
        }))));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters!.RemoveAt(1))));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters![1].Name = "status")));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M4_ParameterTypeChanged_IsMajor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters![0].ValueType = AttributeValueTypesDto.Int)));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters![2].ValueCkRecordId = "Base/Record")));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters![1].ValueCkEnumId = "Base/Enum")));
    }

    [Theory]
    [InlineData("type", CkSemVerLevel.Minor)]
    [InlineData("interface", CkSemVerLevel.Major)] // row I12 (AB#6336)
    public void M5_ParameterMadeRequired_IsMajor_MadeOptional_IsMinor(string owner, CkSemVerLevel relaxed)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Parameters![0].IsOptional = false)));
        Assert.Equal(relaxed, Required(Change(owner, m => m.Parameters![1].IsOptional = true)));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M6_ResultChanged_IsMajor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Result = null)));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Result!.ValueCkRecordId = "Base/Record")));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M7_ErrorCodeRemoved_IsMajor(string owner)
    {
        var classified = Change(owner, m => m.Errors!.RemoveAt(1));

        Assert.Equal(CkSemVerLevel.Major, Required(classified));
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.MethodError, ChangeKind: CkModelChangeKind.Removed });
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M8_ErrorCodeAdded_IsMajor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Errors!.Add(new CkMethodErrorDto { Code = "NEW_ERROR" }))));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M9_KindChanged_IsMajor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Kind = CkMethodKindDto.Static)));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M10_IdempotentTrueToFalse_IsMajor_FalseToTrue_IsMinor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Execution!.Idempotent = false)));

        var baseline = Model();
        Method(baseline, owner).Execution!.Idempotent = false;
        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, Model()));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M11_TimeoutChanged_IsMinor(string owner)
    {
        Assert.Equal(CkSemVerLevel.Minor, Required(Change(owner, m => m.Execution!.TimeoutSeconds = 60)));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M12_AuthorizationStricter_IsMajor_Looser_IsMinor(string owner)
    {
        // stricter
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Roles = [])));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Scopes = [])));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.AllowSelf = false)));
        // mixed: one role removed, another added
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Roles = ["Other"])));
        // looser
        Assert.Equal(CkSemVerLevel.Minor, Required(Change(owner, m => m.Authorization!.Roles = ["UserManagement", "Other"])));

        var baseline = Model();
        Method(baseline, owner).Authorization!.AllowSelf = false;
        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, Model()));
    }

    [Theory]
    [InlineData("type", CkSemVerLevel.Minor)]
    [InlineData("interface", CkSemVerLevel.Major)] // row I12 (AB#6336): sensitive is part of the contract
    public void M13_SensitiveChanged_IsMinor(string owner, CkSemVerLevel expected)
    {
        Assert.Equal(expected, Required(Change(owner, m => m.Parameters![0].Sensitive = false)));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M14_DescriptionsOfMethodParameterAndError_ArePatch(string owner)
    {
        Assert.Equal(CkSemVerLevel.Patch, Required(Change(owner, m => m.Description = "Reworded")));
        Assert.Equal(CkSemVerLevel.Patch, Required(Change(owner, m => m.Parameters![0].Description = "Reworded")));
        Assert.Equal(CkSemVerLevel.Patch, Required(Change(owner, m => m.Errors![0].Description = "Reworded")));
    }
}
