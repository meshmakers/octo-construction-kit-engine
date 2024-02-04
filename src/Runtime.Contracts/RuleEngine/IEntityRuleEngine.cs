using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
///     Interface of the runtime entity validation engine
/// </summary>
public interface IEntityRuleEngine
{
    /// <summary>
    ///     Validates the given update records to the existing repository
    /// </summary>
    /// <param name="tenantId">The tenant the chance is applied</param>
    /// <param name="entityUpdateInfos">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="originFileResolver">Represents the origin file resolver</param>
    /// <param name="operationResult">Represents the operation result with validation messages</param>
    /// <returns></returns>
    Task<EntityRuleEngineResult<TEntity>> ValidateAsync<TEntity>(string tenantId,
        IReadOnlyList<IEntityUpdateInfo<TEntity>> entityUpdateInfos, IOriginFileResolver originFileResolver,
        OperationResult operationResult) where TEntity : RtEntity;
}