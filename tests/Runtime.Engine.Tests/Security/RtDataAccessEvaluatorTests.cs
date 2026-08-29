using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     Classification semantics of <see cref="RtDataAccessEvaluator" /> (AB#4972): protected once
///     targeted, additive grants, All beats OwnedOnly, fail-closed, AuditOnly separation, system
///     bypass, derived-type inheritance via the self-and-base id set.
/// </summary>
public class RtDataAccessEvaluatorTests
{
    private const string DocType = "Meshmakers.Accounting/UploadedDocument";
    private const string OpenType = "Meshmakers.Accounting/BankTransaction";

    private static readonly RtSecurityContext Employee =
        RtSecurityContext.ForUser("user-1", ["AccountingEmployee"]);

    private static readonly RtSecurityContext Management =
        RtSecurityContext.ForUser("user-2", ["AccountingManagement"]);

    private static readonly RtSecurityContext Outsider =
        RtSecurityContext.ForUser("user-3", ["AccountingShareholder"]);

    private static RtDataPolicyTable AccountingTable(bool auditOnly = false)
    {
        return new RtDataPolicyTable(
        [
            new RtDataPolicyRule("accounting.documents", new HashSet<string> { DocType },
                [RtDataAction.Read, RtDataAction.Write, RtDataAction.Delete],
                OwnedOnly: false, AuditOnly: auditOnly, new HashSet<string> { "AccountingManagement" }),
            new RtDataPolicyRule("accounting.documents", new HashSet<string> { DocType },
                [RtDataAction.Read, RtDataAction.Write],
                OwnedOnly: true, AuditOnly: auditOnly, new HashSet<string> { "AccountingEmployee" })
        ]);
    }

    [Fact]
    public void UntargetedType_IsOpen()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [OpenType], RtDataAction.Read,
            Employee, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Open, level);
    }

    [Fact]
    public void EmptyTable_IsOpen()
    {
        var level = RtDataAccessEvaluator.Classify(RtDataPolicyTable.Empty, [DocType], RtDataAction.Read,
            Employee, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Open, level);
    }

    [Fact]
    public void SystemContext_IsAlwaysOpen()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Delete,
            RtSecurityContext.System, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Open, level);
    }

    [Fact]
    public void FullGrant_IsAllowed()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Delete,
            Management, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Allowed, level);
    }

    [Fact]
    public void OwnedGrant_IsOwnedOnly()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Read,
            Employee, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.OwnedOnly, level);
    }

    [Fact]
    public void OwnedGrant_ActionNotGranted_IsDenied()
    {
        // Employee has Read/Write owned-only, but no Delete grant.
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Delete,
            Employee, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Denied, level);
    }

    [Fact]
    public void NoMatchingRole_IsDenied()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Read,
            Outsider, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Denied, level);
    }

    [Fact]
    public void FullGrantBeatsOwnedGrant()
    {
        var both = RtSecurityContext.ForUser("user-4", ["AccountingEmployee", "AccountingManagement"]);
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Read,
            both, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Allowed, level);
    }

    [Fact]
    public void OwnedGrant_WithoutSubject_FailsClosed()
    {
        var noSubject = RtSecurityContext.ForUser(null, ["AccountingEmployee"]);
        var level = RtDataAccessEvaluator.Classify(AccountingTable(), [DocType], RtDataAction.Read,
            noSubject, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Denied, level);
    }

    [Fact]
    public void DerivedType_InheritsViaBaseTypeIds()
    {
        // The queried type is a derived type; DocType is one of its base ids.
        var level = RtDataAccessEvaluator.Classify(AccountingTable(),
            ["Meshmakers.Accounting/EmailUploadedDocument", DocType], RtDataAction.Read,
            Outsider, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Denied, level);
    }

    [Fact]
    public void AuditOnlyPolicies_EnforcementView_StaysOpen()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(auditOnly: true), [DocType],
            RtDataAction.Read, Outsider, includeAuditOnlyPolicies: false);
        Assert.Equal(RtDataAccessLevel.Open, level);
    }

    [Fact]
    public void AuditOnlyPolicies_AuditView_ClassifiesViolation()
    {
        var level = RtDataAccessEvaluator.Classify(AccountingTable(auditOnly: true), [DocType],
            RtDataAction.Read, Outsider, includeAuditOnlyPolicies: true);
        Assert.Equal(RtDataAccessLevel.Denied, level);
    }

    [Fact]
    public void MixedEnforceAndAuditPolicies_EnforceWinsForItsTypes()
    {
        var mixed = new RtDataPolicyTable(
        [
            new RtDataPolicyRule("p.enforced", new HashSet<string> { DocType },
                [RtDataAction.Read], OwnedOnly: false, AuditOnly: false,
                new HashSet<string> { "AccountingManagement" }),
            new RtDataPolicyRule("p.audited", new HashSet<string> { OpenType },
                [RtDataAction.Read], OwnedOnly: false, AuditOnly: true,
                new HashSet<string> { "AccountingManagement" })
        ]);

        Assert.Equal(RtDataAccessLevel.Denied,
            RtDataAccessEvaluator.Classify(mixed, [DocType], RtDataAction.Read, Outsider, false));
        Assert.Equal(RtDataAccessLevel.Open,
            RtDataAccessEvaluator.Classify(mixed, [OpenType], RtDataAction.Read, Outsider, false));
        Assert.Equal(RtDataAccessLevel.Denied,
            RtDataAccessEvaluator.Classify(mixed, [OpenType], RtDataAction.Read, Outsider, true));
    }
}
