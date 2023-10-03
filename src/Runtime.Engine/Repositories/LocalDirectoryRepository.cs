using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
/// Implements the <see cref="ILocalRuntimeRepository"/> interface to manage runtime entities that are located in a directory on the local hard disk.
/// </summary>
internal class LocalDirectoryRepository : ILocalRuntimeRepository
{
    /// <summary>
    /// Creates a new instance of <see cref="LocalDirectoryRepository"/>.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to request services</param>
    /// <param name="directoryPath">Path to directory the runtime entities are located.</param>
    public LocalDirectoryRepository(string tenantId, string directoryPath)
    {
        TenantId = tenantId;
        DirectoryPath = directoryPath;
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

    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId, RtEntityId targetRtEntityId)
    {
        return new RtAssociation
        {
            AssociationRoleId = ckRoleId,
            OriginCkTypeId = originRtEntityId.CkTypeId,
            OriginRtId = originRtEntityId.RtId,
            TargetCkTypeId = targetRtEntityId.CkTypeId,
            TargetRtId = targetRtEntityId.RtId
        };
    }

    public RtEntity CreateTransientRtEntity(CkId<CkTypeId> ckTypeId)
    {
        throw new NotImplementedException();
    }

    public TEntity CreateTransientRtEntity<TEntity>() where TEntity : RtEntity, new()
    {
        throw new NotImplementedException();
    }

    public Task<IOctoSession> GetSessionAsync()
    {
        throw new NotImplementedException();
    }

    public Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId)
    {
        throw new NotImplementedException();
    }

    public string DirectoryPath { get; }
}