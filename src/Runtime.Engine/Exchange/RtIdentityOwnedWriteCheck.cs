using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
///     An edge of an identity association role (<see cref="IdentityOwnedWrites" />) in a user-initiated import.
/// </summary>
/// <param name="CkTypeId">The runtime CK type id of the entity as written in the file</param>
/// <param name="RtId">The rtId of the entity</param>
/// <param name="Path"><c>association &lt;roleId&gt; -&gt; &lt;targetRtId&gt;</c></param>
internal readonly record struct IdentityOwnedWrite(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId, string Path);

/// <summary>
///     AB#8042 / AB#8053: finds the edges of the identity association roles in a user-initiated import (the caller path of
///     <c>ImportRt</c>), on whichever entity of the file they are declared: only the identity service writes them. A pure read
///     of the CK cache, so the import can run it before the first write. The system path (blueprint seeds,
///     <c>ImportModelAsync</c>) is not checked: the Bootstrap seed writes the <c>TenantOwners</c> role edges through it. The
///     identity-owned attributes of <c>System.Identity/User</c> are not checked here any more: since System.Identity 3.0.0 they
///     are <c>access: MethodOnly</c> in the model and rule 139 (<c>RtAttributeAccessCheck</c>) refuses them.
/// </summary>
internal static class RtIdentityOwnedWriteCheck
{
    private const string IdentityModelName = "System.Identity";

    /// <summary>
    ///     True when the tenant has System.Identity (its User type in the CK cache). Lets the caller skip the extra pass over a
    ///     large import file on tenants without identity data.
    /// </summary>
    internal static bool TenantHasIdentityModel(ICkCacheService ckCacheService, string tenantId) =>
        ckCacheService.GetCkTypes(tenantId).Any(t => IsIdentityUser(t.CkTypeId.ToRtCkId()));

    // Version-less: "System.Identity/User", "System.Identity-3.0.0/User" and "System.Identity@3/User" are the same type.
    private static bool IsIdentityUser(RtCkId<CkTypeId> id) =>
        id.ElementId.Name == "User" && id.ModelId.StartsWith(IdentityModelName, StringComparison.Ordinal) &&
        (id.ModelId.Length == IdentityModelName.Length || id.ModelId[IdentityModelName.Length] is '-' or '@');

    /// <summary>Returns one offender per identity-owned value found, in the order of the input; empty when there is none.</summary>
    internal static IReadOnlyList<IdentityOwnedWrite> Find(ICkCacheService ckCacheService, string tenantId,
        IEnumerable<RtEntityTcDto> entities)
    {
        var offenders = new List<IdentityOwnedWrite>();
        foreach (var entity in entities)
        {
            if (entity.CkTypeId == null)
            {
                continue;
            }

            foreach (var association in entity.Associations ?? [])
            {
                if (IdentityOwnedWrites.IsIdentityOwnedAssociation(association.RoleId))
                {
                    offenders.Add(new IdentityOwnedWrite(entity.CkTypeId, entity.RtId,
                        $"association {association.RoleId.FullName} -> {association.TargetRtId}"));
                }
            }
        }

        return offenders;
    }

    /// <summary>The refusal message (parameters rtId, ckTypeId, path).</summary>
    internal static OperationMessage ToMessage(IdentityOwnedWrite offender) =>
        MessageCodes.RtIdentityOwnedWriteRestricted($"{offender.CkTypeId.SemanticVersionedFullName}@{offender.RtId}",
            offender.RtId, offender.CkTypeId.SemanticVersionedFullName, offender.Path);
}
