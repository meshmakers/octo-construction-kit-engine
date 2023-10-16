using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
/// Interface of the runtime graph validation engine
/// </summary>
internal interface IGraphRuleEngine
{
    /// <summary>
    /// Validates the given update records to the existing repository
    /// </summary>
    /// <param name="session">Session object</param>
    /// <param name="repositoryDataSource">Data source repository access object</param>
    /// <param name="entityUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="operationResult">Represents the operation result with validation messages</param>
    /// <returns></returns>
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList, OperationResult operationResult);

    /// <summary>
    /// Validates the given update records of entities and associations to the existing repository
    /// </summary>
    /// <param name="session">Session object</param>
    /// <param name="repositoryDataSource">Data source repository access object</param>
    /// <param name="entityUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="associationUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="operationResult">Represents the operation result with validation messages</param>
    /// <returns></returns>
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult);

    /// <summary>
    /// Validates the given update records of associations to the existing repository
    /// </summary>
    /// <param name="session">Session object</param>
    /// <param name="repositoryDataSource">Data source repository access object</param>
    /// <param name="associationUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="operationResult">Represents the operation result with validation messages</param>
    /// <returns></returns>
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult);
}
