using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
/// Interface for bulk mutation of a data source
/// </summary>
public interface IBulkRtMutation
{
    /// <summary>
    /// Applies the changes to the data source
    /// </summary>
    /// <param name="session"></param>
    /// <param name="repositoryDataSource"></param>
    /// <param name="entityUpdateInfoList"></param>
    /// <param name="associationUpdateInfoList"></param>
    Task ApplyChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList);
}