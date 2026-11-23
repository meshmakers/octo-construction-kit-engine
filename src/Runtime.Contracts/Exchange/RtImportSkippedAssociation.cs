using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// An association edge the last import did not write because its origin or target exists neither in the import nor in
/// the tenant (AB#5005). Blueprint seeds keep these edges and write them once both ends exist (AB#9651).
/// </summary>
/// <param name="RoleId">The association role.</param>
/// <param name="OriginCkTypeId">The CK type of the origin entity.</param>
/// <param name="OriginRtId">The origin entity.</param>
/// <param name="TargetCkTypeId">The CK type of the target entity.</param>
/// <param name="TargetRtId">The target entity.</param>
/// <param name="HasAttributes">True when the edge carries attribute values or target attribute ids.</param>
public sealed record RtImportSkippedAssociation(
    RtCkId<CkAssociationRoleId> RoleId,
    RtCkId<CkTypeId> OriginCkTypeId,
    OctoObjectId OriginRtId,
    RtCkId<CkTypeId> TargetCkTypeId,
    OctoObjectId TargetRtId,
    bool HasAttributes);
