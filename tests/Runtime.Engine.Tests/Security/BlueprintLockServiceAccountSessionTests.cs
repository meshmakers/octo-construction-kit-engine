using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#6392 — ServiceAccount pipeline sessions. The mesh adapter opens the session of a node with
///     <c>Identity: ServiceAccount</c> as <c>RtSecurityContext.ForUser(identity.SubjectId, identity.Roles)</c>
///     (octo-mesh-adapter, <c>PipelineIdentityResolver.ResolveServiceAccountAsync</c>) - NOT as the system
///     context - and only falls back to <c>RtSecurityContext.System</c> when the pipeline has no service
///     account configured (legacy tenants) or the node asks for <c>Identity: System</c>. Only IsSystem
///     sessions bypass the blueprint-lock guard, so a configured service account is guarded like a user
///     (no exemption in v1). These tests pin that classification.
/// </summary>
public class BlueprintLockServiceAccountSessionTests
{
    private const string RuleType = "Test/CategorizationRule";

    private static readonly RtDataPolicyTable Table = new(
    [
        new RtDataPolicyRule("rules", new HashSet<string> { RuleType }, [RtDataAction.Write], OwnedOnly: false,
            AuditOnly: false, new HashSet<string> { "PipelineRole" }, ProtectBlueprintLocked: true)
    ]);

    [Fact]
    public void ConfiguredServiceAccountSession_IsGuardedLikeAUser()
    {
        // what PipelineIdentityResolver builds for a configured service account
        var serviceAccount = RtSecurityContext.ForUser("sa-client-id", ["PipelineRole"]);

        Assert.False(serviceAccount.IsSystem);
        Assert.Equal(RtBlueprintLockProtection.Enforce,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(Table, [RuleType], serviceAccount));
    }

    [Fact]
    public void SystemSession_NoServiceAccountConfigured_OrIdentitySystem_IsNotGuarded()
    {
        Assert.True(RtSecurityContext.System.IsSystem);
        Assert.Equal(RtBlueprintLockProtection.None,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(Table, [RuleType], RtSecurityContext.System));
    }
}
