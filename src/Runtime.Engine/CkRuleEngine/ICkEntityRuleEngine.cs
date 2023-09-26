namespace Meshmakers.Octo.SystematizedData.Persistence.CkModel.CkRuleEngine;

public interface ICkEntityRuleEngine
{
    Task<CkEntityRuleEngineResult> ValidateAsync(IReadOnlyList<EntityUpdateInfo> entityUpdateInfos);
}
