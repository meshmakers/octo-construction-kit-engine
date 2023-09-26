using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine;

/// <summary>
/// Implements the <see cref="ITenantRepositoryInternal"/> interface.
/// </summary>
internal class TenantRepository : ITenantRepositoryInternal
{
    /// <summary>
    /// Creates a new instance of <see cref="TenantRepository"/>.
    /// </summary>
    /// <param name="tenantId"></param>
    public TenantRepository(string tenantId)
    {
        TenantId = tenantId;
    }
    
    public string TenantId { get; }
    public Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId, GraphDirections direction)
    {
        throw new NotImplementedException();
    }

    public Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId, CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction)
    {
        throw new NotImplementedException();
    }

    public Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId, RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId)
    {
        throw new NotImplementedException();
    }

    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId, RtEntityId targetEntityId)
    {
        throw new NotImplementedException();
    }

    public Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId)
    {
        throw new NotImplementedException();
    }
}