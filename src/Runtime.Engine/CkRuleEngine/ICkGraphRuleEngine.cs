using Meshmakers.Octo.SystematizedData.Persistence.DataAccess;

namespace Meshmakers.Octo.SystematizedData.Persistence.CkModel.CkRuleEngine;

internal interface ICkGraphRuleEngine
{
    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList);

    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList);

    Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList);
}
