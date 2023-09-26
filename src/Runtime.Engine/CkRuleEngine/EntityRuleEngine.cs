using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.CkRuleEngine;

/// <summary>
/// Implementation of the runtime entity validation engine
/// </summary>
internal class EntityRuleEngine : IEntityRuleEngine
{
    private readonly ICkCacheService _ckCache;
    private readonly ITenantRepositoryInternal _tenantRepository;

    public EntityRuleEngine(ICkCacheService ckCache, ITenantRepositoryInternal tenantRepository)
    {
        _ckCache = ckCache;
        _tenantRepository = tenantRepository;
    }

    public Task<EntityRuleEngineResult> ValidateAsync(IReadOnlyList<EntityUpdateInfo> entityUpdateInfos)
    {
        var entityValidatorResult = new EntityRuleEngineResult();
        
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
