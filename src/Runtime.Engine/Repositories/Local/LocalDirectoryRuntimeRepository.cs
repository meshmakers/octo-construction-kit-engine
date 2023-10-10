using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
/// Implements the <see cref="ILocalRuntimeRepository"/> interface to manage runtime entities that are located in a directory on the local hard disk.
/// </summary>
internal class LocalDirectoryRuntimeRepository : RuntimeRepositoryBase, ILocalRuntimeRepository
{
    /// <summary>
    /// Creates a new instance of <see cref="LocalDirectoryRuntimeRepository"/>.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to request services</param>
    /// <param name="directoryPath">Path to directory the runtime entities are located.</param>
    /// <param name="ckCacheService">Construction kit cache service</param>
    /// <param name="repositoryDataSource">Data source of a local repository</param>
    /// <param name="entityRuleEngine">Rule engine that validates the model against construction kit</param>
    public LocalDirectoryRuntimeRepository(string tenantId, string directoryPath, ICkCacheService ckCacheService,
        ILocalRepositoryDataSource repositoryDataSource, IEntityRuleEngine entityRuleEngine)
    : base(tenantId, ckCacheService, repositoryDataSource, entityRuleEngine)
    {
        DirectoryPath = directoryPath;
    }


    public override Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId)
    {
        throw new NotImplementedException();
    }

    public override Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId, GraphDirections direction)
    {
        throw new NotImplementedException();
    }

    public override Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId, CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction)
    {
        throw new NotImplementedException();
    }

    public override Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId, RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId)
    {
        throw new NotImplementedException();
    }
    
    public override Task<IOctoSession> GetSessionAsync()
    {
        throw new NotImplementedException();
    }

    public string DirectoryPath { get; }
    
}