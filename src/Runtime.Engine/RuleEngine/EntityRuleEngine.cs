using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.RuleEngine;

/// <summary>
/// Implementation of the runtime entity validation engine
/// </summary>
internal class EntityRuleEngine : IEntityRuleEngine
{
    private readonly ICkCacheService _ckCache;
    private readonly IRuntimeRepository _runtimeRepository;

    public EntityRuleEngine(ICkCacheService ckCache, IRuntimeRepository runtimeRepository)
    {
        _ckCache = ckCache;
        _runtimeRepository = runtimeRepository;
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
