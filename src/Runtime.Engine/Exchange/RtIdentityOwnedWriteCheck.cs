using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
///     A provided value for an identity-owned User field, or an identity association (<see cref="IdentityOwnedWrites" />).
/// </summary>
/// <param name="CkTypeId">The runtime CK type id of the entity as written in the file</param>
/// <param name="RtId">The rtId of the entity</param>
/// <param name="Path">The attribute name, or <c>association &lt;roleId&gt; -&gt; &lt;targetRtId&gt;</c></param>
internal readonly record struct IdentityOwnedWrite(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId, string Path);

/// <summary>
///     AB#8042: finds the values of a user-initiated import (the caller path of <c>ImportRt</c>) that only the identity service
///     may write - the identity-owned attributes of <c>System.Identity/User</c> and the edges of the identity association roles,
///     on whichever entity of the file they are declared. A pure read of the CK cache, so the import can run it before the first
///     write. The system path (blueprint seeds, <c>ImportModelAsync</c>) is not checked: the Bootstrap seed writes the
///     <c>TenantOwners</c> role edges through it.
/// </summary>
internal static class RtIdentityOwnedWriteCheck
{
    /// <summary>
    ///     True when the tenant has System.Identity (its User type in the CK cache). Lets the caller skip the extra pass over a
    ///     large import file on tenants without identity data.
    /// </summary>
    internal static bool TenantHasIdentityModel(ICkCacheService ckCacheService, string tenantId) =>
        ckCacheService.GetCkTypes(tenantId).Any(t => IdentityOwnedWrites.IsUserType(t.CkTypeId.ToRtCkId()));

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

            if (IdentityOwnedWrites.IsUserType(entity.CkTypeId) &&
                ckCacheService.TryGetRtCkType(tenantId, entity.CkTypeId, out var typeGraph) && typeGraph != null)
            {
                foreach (var modelAttribute in entity.Attributes)
                {
                    var attribute = typeGraph.AllAttributes.Values
                        .FirstOrDefault(a => a.CkAttributeId.Equals(modelAttribute.Id));
                    if (attribute != null &&
                        IdentityOwnedWrites.IsIdentityOwnedAttribute(entity.CkTypeId, attribute.AttributeName))
                    {
                        offenders.Add(new IdentityOwnedWrite(entity.CkTypeId, entity.RtId, attribute.AttributeName));
                    }
                }
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
