using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Interface for bulk mutation of a data source
/// </summary>
public interface IBulkRtMutation
{
    /// <summary>
    ///     Applies the changes to the data source
    /// </summary>
    /// <param name="session">Session to use for the operation</param>
    /// <param name="repositoryDataSource">Repository data source to apply changes to</param>
    /// <param name="ckCacheService">Cache service for Construction Kit</param>
    /// <param name="entityUpdateInfoList">List of entity updates to apply</param>
    /// <param name="associationUpdateInfoList">List of association updates to apply</param>
    /// <param name="options">Options for the bulk mutation</param>
    Task ApplyChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, BulkRtMutationOptions options);
}