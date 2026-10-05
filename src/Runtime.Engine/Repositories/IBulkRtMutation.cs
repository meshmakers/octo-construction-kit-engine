using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Interface for bulk mutation of a data source
/// </summary>
public interface IBulkRtMutation
{
    /// <summary>
    ///     The Secret write step this mutation applies to every insert, update and replace (AB#5532).
    ///     Repository paths that write without <see cref="ApplyChangesAsync" /> - bulk import, CK migration
    ///     writes - must run their entities through it as well (see
    ///     <see cref="RuntimeRepositoryBase.BulkInsertRtEntitiesAsync" />).
    /// </summary>
    ISecretWriteNormalizer SecretWriteNormalizer { get; }

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