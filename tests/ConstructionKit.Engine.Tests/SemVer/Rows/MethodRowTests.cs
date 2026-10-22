using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6268 rows M1–M17: per-field verdict for method changes, for a type method and an interface method alike.
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
    public void M15_HandlerChanged_IsMajor_AddingTheDefaultIsNoChange(string owner)
    {
        // service -> pipeline and back: Major both ways.
        var toPipeline = Change(owner, m => m.Execution!.Handler = CkMethodHandlers.Pipeline);
        Assert.Equal(CkSemVerLevel.Major, Required(toPipeline));
        Assert.Contains(toPipeline, c => c.Change is { Property: "handler", ChangeKind: CkModelChangeKind.Modified });

        var pipelineBaseline = Model();
        Method(pipelineBaseline, owner).Execution!.Handler = CkMethodHandlers.Pipeline;
        Assert.Equal(CkSemVerLevel.Major, Level(pipelineBaseline, Model()));

        // An omitted key equals the default: declaring 'service' explicitly is no change.
        Assert.Equal(CkSemVerLevel.None, Required(Change(owner, m => m.Execution!.Handler = CkMethodHandlers.Service)));
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
        // AB#6338 (platform-owner decision 2026-10-10): roles any-of, scopes all-of, DEFAULT-DENY — an omitted block
        // or empty roles admit administrators only. The same rules apply to interface methods (metadata, not part of
        // the invocation contract, row I12).
        // roles: removed / emptied = stricter; added (also to an empty list) = looser
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Roles = [])));
        Assert.Equal(CkSemVerLevel.Minor, Required(Change(owner, m => m.Authorization!.Roles = ["UserManagement", "Other"])));
        var noRoles = Model();
        Method(noRoles, owner).Authorization!.Roles = [];
        Assert.Equal(CkSemVerLevel.Minor, Level(noRoles, Model()));
        // scopes: added = stricter (all-of, gate case 7f), removed = looser
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Scopes = ["extra_scope", "s2"])));
        Assert.Equal(CkSemVerLevel.Minor, Required(Change(owner, m => m.Authorization!.Scopes = [])));
        var noScopes = Model();
        Method(noScopes, owner).Authorization!.Scopes = [];
        Assert.Equal(CkSemVerLevel.Major, Level(noScopes, Model())); // gate case 7c: [] -> [s]
        // allowSelf
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.AllowSelf = false)));
        var noSelf = Model();
        Method(noSelf, owner).Authorization!.AllowSelf = false;
        Assert.Equal(CkSemVerLevel.Minor, Level(noSelf, Model()));
        // mixed: stricter + looser = Major; looser + looser = Minor (the corpus "role added and scope removed")
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Roles = ["Other"])));
        Assert.Equal(CkSemVerLevel.Minor, Required(Change(owner, m =>
        {
            m.Authorization!.Roles = ["UserManagement", "Viewer"];
            m.Authorization.Scopes = [];
        })));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M12_AWidenedAuthorizationIsMinorAndRequiresAnAcknowledgement_AStricterOneDoesNot(string owner)
    {
        // AB#6585: who may invoke a method widens (a role - also Everyone -, a dropped scope, self-calls admitted, a
        // looser block): Minor, but the author must acknowledge it with a reason. Stricter changes are Major anyway.
        foreach (var classified in new[]
                 {
                     Change(owner, m => m.Authorization!.Roles = ["UserManagement", "Everyone"]),
                     Change(owner, m => m.Authorization!.Scopes = [])
                 })
        {
            var change = Assert.Single(classified, c => c.Level == CkSemVerLevel.Minor);
            Assert.True(change.RequiresAcknowledge);
            Assert.Contains("requires acknowledge", change.Reason);
        }

        var noSelf = Model();
        Method(noSelf, owner).Authorization!.AllowSelf = false;
        Assert.True(Assert.Single(Classify(noSelf, Model()), c => c.Change.Property == "allowSelf").RequiresAcknowledge);
        var noBlock = Model();
        Method(noBlock, owner).Authorization = null;
        var withBlock = Model();
        Method(withBlock, owner).Authorization = new CkMethodAuthorizationDto { Roles = ["Everyone"], AllowSelf = true };
        Assert.True(Assert.Single(Classify(noBlock, withBlock), c => c.Change.Property == "authorization").RequiresAcknowledge);

        Assert.All(Change(owner, m => m.Authorization!.Roles = []), c => Assert.False(c.RequiresAcknowledge));
        Assert.All(Change(owner, m => m.Authorization!.Scopes = ["extra_scope", "s2"]), c => Assert.False(c.RequiresAcknowledge));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M12_RoleNamesCompareCaseInsensitively(string owner)
    {
        // P3-3 (platform-owner decision 2026-10-10): like ASP.NET Identity's NormalizedName, 'Admin' and 'admin' are the
        // same role — a change that only alters case is no change.
        Assert.Empty(Change(owner, m => m.Authorization!.Roles = ["usermanagement"]));
        Assert.Empty(Change(owner, m => m.Authorization!.Roles = ["USERMANAGEMENT", "USERMANAGEMENT"]));

        // A case twin next to the real role adds no role; a really new role is looser; a removed role is stricter.
        Assert.Empty(Change(owner, m => m.Authorization!.Roles = ["UserManagement", "usermanagement"]));
        Assert.Equal(CkSemVerLevel.Minor, Required(Change(owner, m => m.Authorization!.Roles = ["usermanagement", "Ops"])));
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Roles = [])));

        // Block added or removed: the case of the roles does not matter either (case-twin roles are a no-op).
        var baseline = Model();
        Method(baseline, owner).Authorization = new CkMethodAuthorizationDto { Roles = ["Ops"], AllowSelf = true };
        var current = Model();
        Method(current, owner).Authorization = new CkMethodAuthorizationDto { Roles = ["OPS"], AllowSelf = true };
        Assert.Empty(Classify(baseline, current));

        // Scopes stay case-sensitive.
        Assert.Equal(CkSemVerLevel.Major, Required(Change(owner, m => m.Authorization!.Scopes = ["EXTRA_SCOPE"])));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M12_AuthorizationBlockRemovedOrAdded_IsOneChange_UnderDefaultDeny(string owner)
    {
        // Gate case 7a: removing the whole block is ONE change (no contradictory field lines). Under default-deny it
        // empties the roles and drops self-calls: stricter, Major.
        var removed = Change(owner, m => m.Authorization = null);
        var authorization = Assert.Single(removed, c => c.Change.Property is "authorization" or "roles" or "scopes" or "allowSelf");
        Assert.Equal("authorization", authorization.Change.Property);
        Assert.Equal(CkSemVerLevel.Major, authorization.Level);
        Assert.Contains("default-deny", authorization.Reason);

        // Adding a block that only grants roles (and self-calls) is looser: Minor, listed as a security change.
        var baseline = Model();
        Method(baseline, owner).Authorization = null;
        var current = Model();
        Method(current, owner).Authorization = new CkMethodAuthorizationDto { Roles = ["Ops"], AllowSelf = true };
        var added = Assert.Single(Classify(baseline, current), c => c.Change.Property == "authorization");
        Assert.Equal(CkSemVerLevel.Minor, added.Level);
        Assert.True(added.IsBehavioural);
        Assert.Contains("security: method access widened", added.Reason);

        // P3-2: a block that grants nothing means the same as no block: no level, no security note.
        var empty = Model();
        Method(empty, owner).Authorization = new CkMethodAuthorizationDto();
        var noBlock = Model();
        Method(noBlock, owner).Authorization = null;
        Assert.Equal(CkSemVerLevel.None, Level(noBlock, empty));
        Assert.Equal(CkSemVerLevel.None, Level(empty, noBlock));

        // Strict default-deny: an omitted block admits no self-calls, so adding a block without allowSelf is looser too.
        current = Model();
        Method(current, owner).Authorization = new CkMethodAuthorizationDto { Roles = ["Ops"], AllowSelf = false };
        Assert.Equal(CkSemVerLevel.Minor, Level(baseline, current));

        // Adding a block that requires a scope is stricter.
        current = Model();
        Method(current, owner).Authorization = new CkMethodAuthorizationDto { Roles = ["Ops"], AllowSelf = true, Scopes = ["s1"] };
        Assert.Equal(CkSemVerLevel.Major, Level(baseline, current));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M12_LooserAuthorization_IsListedAsBehaviouralSecurityChange(string owner)
    {
        foreach (var classified in new[]
                 {
                     Change(owner, m => m.Authorization!.Roles = ["UserManagement", "Other"]),
                     Change(owner, m => m.Authorization!.Scopes = [])
                 })
        {
            var change = Assert.Single(classified, c => c.Level == CkSemVerLevel.Minor);
            Assert.True(change.IsBehavioural);
            Assert.Contains("security: method access widened", change.Reason);
        }

        Assert.All(Change(owner, m => m.Authorization!.Roles = []).Where(c => c.Level == CkSemVerLevel.Major),
            c => Assert.False(c.IsBehavioural));
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

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M16_ReplacesGenericCreate_FalseToTrueIsMajor_TrueToFalseIsMinor(string owner)
    {
        // absent / false -> true: callers that create entities generically break.
        var toTrue = Change(owner, m => m.ReplacesGenericCreate = true);
        Assert.Equal(CkSemVerLevel.Major, Required(toTrue));
        Assert.Contains(toTrue, c => c.Change is { Property: "replacesGenericCreate", ChangeKind: CkModelChangeKind.Modified, NewValue: "true" });

        // true -> false: the generic create is available again.
        var trueBaseline = Model();
        Method(trueBaseline, owner).ReplacesGenericCreate = true;
        Assert.Equal(CkSemVerLevel.Minor, Level(trueBaseline, Model()));

        // An unchanged flag is no change.
        var both = Model();
        Method(both, owner).ReplacesGenericCreate = true;
        Assert.Equal(CkSemVerLevel.None, Level(trueBaseline, both));
    }

    [Fact]
    public void M16_NewMethodWithTheFlagOnAnExistingType_IsMajor()
    {
        // The type existed in the previous version: its callers create entities generically today.
        var current = Model();
        Methods(current, "type").Add(new CkMethodDto
        {
            MethodId = "Provision-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true
        });

        var classified = Classify(Model(), current);
        Assert.Equal(CkSemVerLevel.Major, Required(classified));
        Assert.Contains(classified, c => c.Change is { ElementKind: CkModelElementKind.TypeMethod, ChangeKind: CkModelChangeKind.Added });
        Assert.Contains(classified, c => c is { Level: CkSemVerLevel.Major, Change: { Property: "replacesGenericCreate", NewValue: "true" } });
    }

    [Fact]
    public void M16_NewMethodWithoutTheFlagOnAnExistingType_StaysMinor()
    {
        var current = Model();
        Methods(current, "type").Add(new CkMethodDto { MethodId = "Provision-1", Kind = CkMethodKindDto.Static });

        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
    }

    [Fact]
    public void M16_NewMethodWithTheFlagOnANewType_IsMinor()
    {
        // A type that did not exist in the previous version has no callers that create it generically.
        var current = Model();
        current.Types!.Add(new CkCompiledTypeDto
        {
            TypeId = "Fresh", DerivedFromCkTypeId = "Base/Entity",
            Methods = [new() { MethodId = "Provision-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true }]
        });

        var classified = Classify(Model(), current);
        Assert.Equal(CkSemVerLevel.Minor, Required(classified));
        Assert.DoesNotContain(classified, c => c.Change.Property == "replacesGenericCreate");
    }

    [Fact]
    public void M17_ResultSensitive_FalseToTrue_IsMinorWithoutAcknowledge()
    {
        var classified = Change("type", m => m.Result!.Sensitive = true);

        Assert.Equal(CkSemVerLevel.Minor, Required(classified));
        var change = Assert.Single(classified, c => c.Change.Property == "resultSensitive");
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.Equal("true", change.Change.NewValue);
        Assert.False(change.RequiresAcknowledge);
        Assert.False(change.IsBehavioural);
        // The result itself did not change (that would be row M6, Major).
        Assert.DoesNotContain(classified, c => c.Change.Property == "result");
    }

    [Fact]
    public void M17_ResultSensitive_TrueToFalse_IsMinorAndRequiresAnAcknowledgement()
    {
        var baseline = Model();
        Method(baseline, "type").Result!.Sensitive = true;

        var change = Assert.Single(Classify(baseline, Model()), c => c.Change.Property == "resultSensitive");

        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.True(change.RequiresAcknowledge);
        Assert.True(change.IsBehavioural);
        Assert.Contains("result redaction removed", change.Reason);
        // The change key an author acknowledges.
        Assert.EndsWith("/ChangePassword-1", change.Change.ElementId, StringComparison.Ordinal);
        Assert.Equal("resultSensitive", change.Change.Property);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M18_DataAccess_WriteToRead_IsMinorAndRequiresAnAcknowledgement(string owner)
    {
        // Omitted = Write; declaring Read lets a caller with only a read grant call the method (AB#7036).
        var classified = Change(owner, m => m.Authorization!.DataAccess = CkMethodDataAccessDto.Read);

        var change = Assert.Single(classified, c => c.Change.Property == "dataAccess");
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.Equal("Write", change.Change.OldValue);
        Assert.Equal("Read", change.Change.NewValue);
        Assert.True(change.RequiresAcknowledge);
        Assert.True(change.IsBehavioural);
        Assert.Contains("method access widened", change.Reason);
        Assert.Contains("row M18", change.Reason);
        Assert.Equal(CkSemVerLevel.Minor, Required(classified));
        // The change key an author acknowledges.
        Assert.EndsWith("/ChangePassword-1", change.Change.ElementId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M18_DataAccess_ReadToWrite_IsMajorAndNotAcknowledgeable(string owner)
    {
        var baseline = Model();
        Method(baseline, owner).Authorization!.DataAccess = CkMethodDataAccessDto.Read;

        var change = Assert.Single(Classify(baseline, Model()), c => c.Change.Property == "dataAccess");

        Assert.Equal(CkSemVerLevel.Major, change.Level);
        Assert.False(change.RequiresAcknowledge);
        Assert.Equal("Read", change.Change.OldValue);
        Assert.Equal("Write", change.Change.NewValue);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("interface")]
    public void M18_DataAccess_ExplicitWriteEqualsOmitted_IsNoChange(string owner)
    {
        Assert.Empty(Change(owner, m => m.Authorization!.DataAccess = CkMethodDataAccessDto.Write));

        // Also when the authorization block is added or removed around an unchanged (default) data access.
        var noBlock = Model();
        Method(noBlock, owner).Authorization = null;
        var onlyDataAccess = Model();
        Method(onlyDataAccess, owner).Authorization = new CkMethodAuthorizationDto { DataAccess = CkMethodDataAccessDto.Write };
        Assert.DoesNotContain(Classify(noBlock, onlyDataAccess), c => c.Change.Property == "dataAccess");
    }

    [Fact]
    public void M18_DataAccess_ReadDeclaredWithTheBlockAdded_IsStillItsOwnMinorChange()
    {
        var noBlock = Model();
        Method(noBlock, "type").Authorization = null;
        var readOnly = Model();
        Method(readOnly, "type").Authorization = new CkMethodAuthorizationDto { DataAccess = CkMethodDataAccessDto.Read };

        var classified = Classify(noBlock, readOnly);

        var change = Assert.Single(classified, c => c.Change.Property == "dataAccess");
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.True(change.RequiresAcknowledge);
        // The block itself grants nothing (no roles, scopes, self-calls): row M12 says None.
        Assert.All(classified.Where(c => c.Change.Property == "authorization"), c => Assert.Equal(CkSemVerLevel.None, c.Level));
    }

    [Fact]
    public void M18_NewMethodThatDeclaresRead_IsAMethodAddition()
    {
        var current = Model();
        Methods(current, "type").Add(new CkMethodDto
        {
            MethodId = "Peek-1", Authorization = new CkMethodAuthorizationDto { Roles = ["Admin"], DataAccess = CkMethodDataAccessDto.Read }
        });

        // Row M1: a new type method is Minor; a new method has no previous data access to widen.
        Assert.Equal(CkSemVerLevel.Minor, Level(Model(), current));
        Assert.DoesNotContain(Classify(Model(), current), c => c.Change.Property == "dataAccess");
    }

    [Fact]
    public void M17_ResultSensitive_Unchanged_IsNoChange()
    {
        var baseline = Model();
        Method(baseline, "type").Result!.Sensitive = true;
        var current = Model();
        Method(current, "type").Result!.Sensitive = true;

        Assert.Equal(CkSemVerLevel.None, Level(baseline, current));
    }

    [Fact]
    public void M17_InterfaceMethod_EitherDirection_IsMajorViaI12_AndNotAcknowledgeable()
    {
        var toTrue = Change("interface", m => m.Result!.Sensitive = true);
        Assert.Equal(CkSemVerLevel.Major, Required(toTrue));

        var baseline = Model();
        Method(baseline, "interface").Result!.Sensitive = true;
        var toFalse = Classify(baseline, Model());
        Assert.Equal(CkSemVerLevel.Major, Required(toFalse));
        Assert.All(toFalse, c => Assert.False(c.RequiresAcknowledge));
        Assert.Contains(toFalse, c => c.Change.Property == "resultSensitive" && c.Reason.Contains("row I12"));
    }

    private static CkCompiledModelRoot Change2(Action<CkMethodDto> change)
    {
        var current = Model();
        change(Method(current, "type"));
        return current;
    }

    [Fact]
    public void M17_NewMethodWithASensitiveResult_IsMinor_AndRemovingTheResultIsOnlyARowM6Change()
    {
        var current = Model();
        Methods(current, "type").Add(new CkMethodDto
        {
            MethodId = "IssueToken-1",
            Result = new CkMethodResultDto { ValueType = AttributeValueTypesDto.String, Sensitive = true }
        });
        var added = Classify(Model(), current);
        Assert.Equal(CkSemVerLevel.Minor, Required(added));
        Assert.DoesNotContain(added, c => c.Change.Property == "resultSensitive");

        // A result removed from a method that had a sensitive one is one "result" change (Major, row M6), not a
        // second sensitive change.
        var baseline = Model();
        Method(baseline, "type").Result!.Sensitive = true;
        var removed = Classify(baseline, Change2(m => m.Result = null));
        Assert.Equal(CkSemVerLevel.Major, Required(removed));
        Assert.DoesNotContain(removed, c => c.Change.Property == "resultSensitive");
    }
}
