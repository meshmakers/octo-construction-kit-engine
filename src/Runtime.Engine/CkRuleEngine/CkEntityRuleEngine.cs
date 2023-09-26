using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.SystematizedData.Persistence.DataAccess;

namespace Meshmakers.Octo.SystematizedData.Persistence.CkModel.CkRuleEngine;

public class CkEntityRuleEngine : ICkEntityRuleEngine
{
    private readonly ICkCacheService _ckCache;
    private readonly ITenantRepositoryInternal _tenantRepository;

    public CkEntityRuleEngine(ICkCacheService ckCache, ITenantRepositoryInternal tenantRepository)
    {
        _ckCache = ckCache;
        _tenantRepository = tenantRepository;
    }

    public Task<CkEntityRuleEngineResult> ValidateAsync(IReadOnlyList<EntityUpdateInfo> entityUpdateInfos)
    {
        var entityValidatorResult = new CkEntityRuleEngineResult();
        
        entityValidatorResult.RtEntitiesToCreate.AddRange(entityUpdateInfos
            .Where(e => e.ModOption == EntityModOptions.Create).Select(e => e.RtEntity));
        entityValidatorResult.RtEntitiesToUpdate.AddRange(entityUpdateInfos
            .Where(e => e.ModOption == EntityModOptions.Update).Select(e => e.RtEntity));
        entityValidatorResult.RtEntitiesToDelete.AddRange(entityUpdateInfos
            .Where(e => e.ModOption == EntityModOptions.Delete).Select(e => e.RtEntity));

        // Currently, no rules are defined.

        return Task.FromResult(entityValidatorResult);
    }
}
