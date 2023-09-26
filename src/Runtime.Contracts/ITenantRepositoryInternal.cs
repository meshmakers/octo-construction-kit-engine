using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace  Meshmakers.Octo.SystematizedData.Persistence.DataAccess;

public interface ITenantRepositoryInternal
{
    string TenantId { get; }
    Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, string toString, GraphDirections any);
    Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId originRtId, CkId<CkAssociationRoleId> key, GraphDirections outbound);
    Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId origin, RtEntityId target, CkId<CkAssociationRoleId> ckRoleId);
    RtAssociation CreateTransientRtAssociation(RtEntityId rtEntityId, CkId<CkAssociationRoleId> roleId, RtEntityId rtEntityId1);
    Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId);
}