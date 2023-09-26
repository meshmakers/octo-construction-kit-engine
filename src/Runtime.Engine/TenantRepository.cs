using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.DataAccess;
using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace Runtime.Engine;

public class TenantRepository : ITenantRepositoryInternal
{
    public TenantRepository(string tenantId)
    {
        TenantId = tenantId;
    }
    
    public string TenantId { get; }
    public Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, string toString, GraphDirections any)
    {
        throw new NotImplementedException();
    }

    public Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId originRtId, CkId<CkAssociationRoleId> key, GraphDirections outbound)
    {
        throw new NotImplementedException();
    }

    public Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId origin, RtEntityId target, CkId<CkAssociationRoleId> ckRoleId)
    {
        throw new NotImplementedException();
    }

    public RtAssociation CreateTransientRtAssociation(RtEntityId rtEntityId, CkId<CkAssociationRoleId> roleId, RtEntityId rtEntityId1)
    {
        throw new NotImplementedException();
    }

    public Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId)
    {
        throw new NotImplementedException();
    }
}