using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
/// Interface of the runtime entity validation engine
/// </summary>
public interface IEntityRuleEngine
{
    /// <summary>
    /// Validates the given update records to the existing repository
    /// </summary>
    /// <param name="tenantId">The tenant the chance is applied</param>
    /// <param name="entityUpdateInfos">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="operationResult">Represents the operation result with validation messages</param>
    /// <returns></returns>
    Task<EntityRuleEngineResult> ValidateAsync(string tenantId, IReadOnlyList<EntityUpdateInfo> entityUpdateInfos,
        OperationResult operationResult);
}
