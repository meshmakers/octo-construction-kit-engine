using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

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
    /// <param name="bulkRtMutation">Bulk runtime mutation implementation</param>
    public LocalDirectoryRuntimeRepository(string tenantId, string directoryPath, ICkCacheService ckCacheService,
        ILocalRepositoryDataSource repositoryDataSource, IBulkRtMutation bulkRtMutation)
    : base(tenantId, ckCacheService, repositoryDataSource, bulkRtMutation)
    {
        DirectoryPath = directoryPath;
    }

    public override Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId, GraphDirections direction)
    {
        throw new NotImplementedException();
    }

    public override Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId, CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction)
    {
        throw new NotImplementedException();
    }

    public override Task<IOctoSession> GetSessionAsync()
    {
        return Task.FromResult((IOctoSession)new LocalSession());
    }

    public string DirectoryPath { get; }
    
}