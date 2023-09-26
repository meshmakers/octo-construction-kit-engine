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
    /// <param name="entityUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <returns></returns>
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList);

    /// <summary>
    /// Validates the given update records of entities and associations to the existing repository
    /// </summary>
    /// <param name="session">Session object</param>
    /// <param name="entityUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <param name="associationUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <returns></returns>
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList);

    /// <summary>
    /// Validates the given update records of associations to the existing repository
    /// </summary>
    /// <param name="session">Session object</param>
    /// <param name="associationUpdateInfoList">A list of entity transactions that should be applied to the existing repository.</param>
    /// <returns></returns>
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList);
}
