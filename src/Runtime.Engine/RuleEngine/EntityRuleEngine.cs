using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Messages;

namespace Meshmakers.Octo.Runtime.Engine.RuleEngine;

/// <summary>
/// Implementation of the runtime entity validation engine
/// </summary>
internal class EntityRuleEngine : IEntityRuleEngine
{
    private readonly ICkCacheService _ckCache;

    public EntityRuleEngine(ICkCacheService ckCache)
    {
        _ckCache = ckCache;
    }

    public async Task<EntityRuleEngineResult> ValidateAsync(string tenantId, IReadOnlyList<EntityUpdateInfo> entityUpdateInfos,
        OperationResult operationResult)
    {

        var entitiesToCreate = new ConcurrentBag<RtEntity>();
        var entitiesToUpdate = new ConcurrentBag<RtEntity>();
        var entitiesToDelete = new ConcurrentBag<RtEntity>();

        await Parallel.ForEachAsync(entityUpdateInfos, (info, token) =>
        {
            if (!_ckCache.TryGetCkType(tenantId, info.RtEntity.CkTypeId, out var ckTypeGraph))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdNotFound(tenantId, info.RtEntity.CkTypeId));
                return ValueTask.CompletedTask;
            }
            
            // check if all attributes are applied that are mandatory. If there is a mandatory attribute missing and no default value is set, throw an exception
            bool isInError = false;
            foreach (var attribute in ckTypeGraph.AllAttributes.Values)
            {
                if (!attribute.IsOptional && !info.RtEntity.Attributes.ContainsKey(attribute.AttributeName))
                {
                    if (attribute.DefaultValues != null)
                    {
                        info.RtEntity.SetAttributeValue(attribute.AttributeName, attribute.ValueType, attribute.DefaultValues);
                    }
                    else
                    {
                        operationResult.AddMessage(MessageCodes.MandatoryAttributeMissing(tenantId,
                            attribute.CkAttributeId, info.RtEntity.CkTypeId, info.RtEntity.RtId));
                        isInError = true;
                    }
                }
            }
            
            token.ThrowIfCancellationRequested();

            if (isInError)
            {
                return ValueTask.CompletedTask;
            }

            switch (info.ModOption)
            {
                case EntityModOptions.Create:
                    entitiesToCreate.Add(info.RtEntity);
                    break;
                case EntityModOptions.Update:
                    entitiesToUpdate.Add(info.RtEntity);
                    break;
                case EntityModOptions.Delete:
                    entitiesToDelete.Add(info.RtEntity);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown mod option '{info.ModOption}'");
            }
            
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
        
        var entityValidatorResult = new EntityRuleEngineResult();
        entityValidatorResult.RtEntitiesToCreate.AddRange(entitiesToCreate);
        entityValidatorResult.RtEntitiesToUpdate.AddRange(entitiesToUpdate);
        entityValidatorResult.RtEntitiesToDelete.AddRange(entitiesToDelete);

        return entityValidatorResult;
    }
}
