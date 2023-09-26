namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
/// Interface of the runtime entity validation engine
/// </summary>
public interface IEntityRuleEngine
{
    /// <summary>
    /// Validates the given update records to the existing repository
    /// </summary>
    /// <param name="entityUpdateInfos">A list of entity transactions that should be applied to the existing repository.</param>
    /// <returns></returns>
    Task<EntityRuleEngineResult> ValidateAsync(IReadOnlyList<EntityUpdateInfo> entityUpdateInfos);
}
