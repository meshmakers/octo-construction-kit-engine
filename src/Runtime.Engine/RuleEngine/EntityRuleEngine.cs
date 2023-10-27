using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
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

    public async Task<EntityRuleEngineResult<TEntity>> ValidateAsync<TEntity>(string tenantId,
        IReadOnlyList<IEntityUpdateInfo<TEntity>> entityUpdateInfos, OperationResult operationResult) where TEntity : RtEntity
    {
        var entitiesToCreate = new ConcurrentBag<TEntity>();
        var entitiesToUpdate = new ConcurrentDictionary<RtEntityId, TEntity>();
        var entitiesToReplace = new ConcurrentDictionary<RtEntityId, TEntity>();
        var entitiesToDelete = new ConcurrentBag<RtEntityId>();

        await Parallel.ForEachAsync(entityUpdateInfos, (info, token) =>
        {
            if (!_ckCache.TryGetCkType(tenantId, info.RtEntityId.CkTypeId, out var ckTypeGraph))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdNotFound(tenantId, info.RtEntityId.CkTypeId));
                return ValueTask.CompletedTask;
            }

            if (ckTypeGraph.IsAbstract)
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdIsAbstract(tenantId, info.RtEntityId.CkTypeId));
                return ValueTask.CompletedTask;
            }

            // check if all attributes are applied that are mandatory. If there is a mandatory attribute missing and no default value is set, throw an exception
            bool isInError = false;
            if (info.ModOption == EntityModOptions.Insert || info.ModOption == EntityModOptions.Replace)
            {
                foreach (var attribute in ckTypeGraph.AllAttributes.Values)
                {
                    if (!attribute.IsOptional && info.RtEntity != null && (!info.RtEntity.Attributes.ContainsKey(attribute.AttributeName) ||
                                                                           info.RtEntity.Attributes[attribute.AttributeName] == null))
                    {
                        if (attribute.DefaultValues != null)
                        {
                            switch (attribute.ValueType)
                            {
                                case AttributeValueTypesDto.IntArray:
                                case AttributeValueTypesDto.RecordArray:
                                case AttributeValueTypesDto.StringArray:
                                    info.RtEntity.SetAttributeValue(attribute.AttributeName, attribute.ValueType, attribute.DefaultValues);
                                    break;
                                default:
                                    info.RtEntity.SetAttributeValue(attribute.AttributeName, attribute.ValueType, attribute.DefaultValues.FirstOrDefault());
                                    break;
                            }
                        }
                        else
                        {
                            operationResult.AddMessage(MessageCodes.MandatoryAttributeMissing(tenantId,
                                attribute.CkAttributeId, info.RtEntity.CkTypeId, info.RtEntity.RtId));
                            isInError = true;
                        }
                    }
                }
            }
            else if (info.ModOption == EntityModOptions.Update)
            {
                foreach (var attribute in ckTypeGraph.AllAttributes.Values)
                {
                    if (!attribute.IsOptional && info.RtEntity != null &&
                        info.RtEntity.Attributes.ContainsKey(attribute.AttributeName) &&
                        info.RtEntity.Attributes[attribute.AttributeName] == null)
                    {
                        operationResult.AddMessage(MessageCodes.MandatoryAttributeMissingAtUpdate(tenantId,
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
                case EntityModOptions.Insert:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsertUpdateReplace(tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
                        return ValueTask.CompletedTask;
                    }

                    entitiesToCreate.Add(info.RtEntity);
                    break;
                case EntityModOptions.Update:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsertUpdateReplace(tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
                        return ValueTask.CompletedTask;
                    }

                    if (!entitiesToUpdate.TryAdd(info.RtEntityId, info.RtEntity))
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityIdAlreadyExistInUpdateList(tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
                        return ValueTask.CompletedTask;
                    }

                    break;
                case EntityModOptions.Replace:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsertUpdateReplace(tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
                        return ValueTask.CompletedTask;
                    }

                    if (!entitiesToReplace.TryAdd(info.RtEntityId, info.RtEntity))
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityIdAlreadyExistInUpdateList(tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
                        return ValueTask.CompletedTask;
                    }

                    break;
                case EntityModOptions.Delete:
                    entitiesToDelete.Add(info.RtEntityId);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown mod option '{info.ModOption}'");
            }

            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        var entityValidatorResult =
            new EntityRuleEngineResult<TEntity>(entitiesToCreate.ToList(),
                entitiesToUpdate.ToDictionary(k => k.Key, v => v.Value),
                entitiesToReplace.ToDictionary(k => k.Key, v => v.Value),
                entitiesToDelete.ToList());

        return entityValidatorResult;
    }
}