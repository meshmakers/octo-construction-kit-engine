using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Messages;

namespace Meshmakers.Octo.Runtime.Engine.RuleEngine;

/// <summary>
///     Implementation of the runtime entity validation engine
/// </summary>
internal class EntityRuleEngine(ICkCacheService ckCache) : IEntityRuleEngine
{
#if NETSTANDARD2_0
    public Task<EntityRuleEngineResult<TEntity>> ValidateAsync<TEntity>(string tenantId,
#else
    public async Task<EntityRuleEngineResult<TEntity>> ValidateAsync<TEntity>(string tenantId,
#endif
        IReadOnlyList<IEntityUpdateInfo<TEntity>> entityUpdateInfos, IOriginFileResolver originFileResolver,
        OperationResult operationResult) where TEntity : RtEntity
    {
        var entitiesToCreate = new ConcurrentBag<TEntity>();
        var entitiesToUpdate = new ConcurrentDictionary<RtEntityId, TEntity>();
        var entitiesToReplace = new ConcurrentDictionary<RtEntityId, TEntity>();
        var entitiesToDelete = new ConcurrentBag<RtEntityId>();

#if NETSTANDARD2_0
        Parallel.ForEach(entityUpdateInfos, (info, _) =>
#else
        await Parallel.ForEachAsync(entityUpdateInfos, (info, token) =>
#endif
        {
            if (!ckCache.TryGetCkType(tenantId, info.RtEntityId.CkTypeId, out var ckTypeGraph))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdNotFound(originFileResolver.Resolve(tenantId), tenantId,
                    info.RtEntityId.CkTypeId));
#if NETSTANDARD2_0
                return;
#else
                return ValueTask.CompletedTask;
#endif
            }

#if NETSTANDARD2_0
            if (ckTypeGraph == null)
            {
                return;
            }
#endif
            
            if (ckTypeGraph.IsAbstract)
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdIsAbstract(originFileResolver.Resolve(tenantId),
                    tenantId,
                    info.RtEntityId.CkTypeId));
#if NETSTANDARD2_0
                return;
#else
                return ValueTask.CompletedTask;
#endif
            }

            // check if all attributes are applied that are mandatory. If there is a mandatory attribute missing and no default value is set, throw an exception
            var isInError = false;
            if (info.ModOption == EntityModOptions.Insert || info.ModOption == EntityModOptions.Replace)
            {
                if (info.RtEntity != null)
                {
                    isInError |= SetDefaultValuesOnInsert(tenantId, ckTypeGraph.AllAttributes.Values.ToList(),
                        info.RtEntity, originFileResolver, operationResult,
                        $"{info.RtEntity.CkTypeId}@{info.RtEntity.RtId}");
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
                        operationResult.AddMessage(MessageCodes.MandatoryAttributeMissingAtUpdate(
                            originFileResolver.Resolve(tenantId),
                            tenantId,
                            attribute.CkAttributeId,
                            info.RtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
                            info.RtEntity.RtId));
                        isInError = true;
                    }
                }
            }

#if !NETSTANDARD2_0
            token.ThrowIfCancellationRequested();
#endif
            if (isInError)
            {
#if NETSTANDARD2_0
                return;
#else
                return ValueTask.CompletedTask;
#endif
            }

            switch (info.ModOption)
            {
                case EntityModOptions.Insert:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsertUpdateReplace(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
#if NETSTANDARD2_0
                        return;
#else
                        return ValueTask.CompletedTask;
#endif
                    }

                    entitiesToCreate.Add(info.RtEntity);
                    break;
                case EntityModOptions.Update:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsertUpdateReplace(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
#if NETSTANDARD2_0
                        return;
#else
                        return ValueTask.CompletedTask;
#endif
                    }

                    if (!entitiesToUpdate.TryAdd(info.RtEntityId, info.RtEntity))
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityIdAlreadyExistInUpdateList(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
#if !NETSTANDARD2_0
                        return ValueTask.CompletedTask;
#endif
                    }

                    break;
                case EntityModOptions.Replace:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsertUpdateReplace(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
#if NETSTANDARD2_0
                        return;
#else
                        return ValueTask.CompletedTask;
#endif
                    }

                    if (!entitiesToReplace.TryAdd(info.RtEntityId, info.RtEntity))
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityIdAlreadyExistInUpdateList(
                            originFileResolver.Resolve(tenantId),
                            tenantId,
                            info.RtEntityId.CkTypeId, info.RtEntityId.RtId));
#if !NETSTANDARD2_0
                        return ValueTask.CompletedTask;
#endif
                    }

                    break;
                case EntityModOptions.Delete:
                    entitiesToDelete.Add(info.RtEntityId);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown mod option '{info.ModOption}'");
            }

#if NETSTANDARD2_0
        });
#else
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
#endif          

        var entityValidatorResult =
            new EntityRuleEngineResult<TEntity>(entitiesToCreate.ToList(),
                entitiesToUpdate.ToDictionary(k => k.Key, v => v.Value),
                entitiesToReplace.ToDictionary(k => k.Key, v => v.Value),
                entitiesToDelete.ToList());

#if NETSTANDARD2_0
        return Task.FromResult(entityValidatorResult);
#else
        return entityValidatorResult;
#endif          
    }

    private bool SetDefaultValuesOnInsert(string tenantId, ICollection<CkTypeAttributeGraph> attributeGraphs, RtTypeWithAttributes rtType,
        IOriginFileResolver originFileResolver, OperationResult operationResult, string reference)
    {
        var isInError = false;
        foreach (var attribute in attributeGraphs)
        {
            if (!attribute.IsOptional && (!rtType.Attributes.ContainsKey(attribute.AttributeName) ||
                                          rtType.Attributes[attribute.AttributeName] == null))
            {
                if (attribute.DefaultValues != null)
                {
                    switch (attribute.ValueType)
                    {
                        case AttributeValueTypesDto.IntArray:
                        case AttributeValueTypesDto.RecordArray:
                        case AttributeValueTypesDto.StringArray:
                            rtType.SetAttributeValue(attribute.AttributeName, attribute.ValueType, attribute.DefaultValues);
                            break;
                        default:
                            rtType.SetAttributeValue(attribute.AttributeName, attribute.ValueType,
                                attribute.DefaultValues.FirstOrDefault());
                            break;
                    }
                }
                else
                {
                    operationResult.AddMessage(MessageCodes.MandatoryAttributeMissing(originFileResolver.Resolve(tenantId), tenantId,
                        attribute.CkAttributeId, reference));
                    isInError = true;
                }
            }

            if (rtType.Attributes.ContainsKey(attribute.AttributeName))
            {
                if (attribute.ValueType == AttributeValueTypesDto.RecordArray)
                {
                    var t = (IEnumerable<object>?)rtType.Attributes[attribute.AttributeName];
                    if (t != null)
                    {
                        foreach (var o in t)
                        {
                            var rtRecord = (RtRecord)o;
                            if (!ckCache.TryGetCkRecord(tenantId, rtRecord.CkRecordId, out var ckRecordGraph))
                            {
                                operationResult.AddMessage(MessageCodes.CkRecordIdNotFound(originFileResolver.Resolve(tenantId), tenantId,
                                    rtRecord.CkRecordId));
                                continue;
                            }

                            isInError |= SetDefaultValuesOnInsert(tenantId, ckRecordGraph!.AllAttributes.Values.ToList(), rtRecord,
                                originFileResolver,
                                operationResult, reference + $"/{attribute.AttributeName}");
                        }
                    }
                }
                else if (attribute.ValueType == AttributeValueTypesDto.Record)
                {
                    var rtRecord = (RtRecord?)rtType.Attributes[attribute.AttributeName];
                    if (rtRecord != null)
                    {
                        if (!ckCache.TryGetCkRecord(tenantId, rtRecord.CkRecordId, out var ckRecordGraph))
                        {
                            operationResult.AddMessage(MessageCodes.CkRecordIdNotFound(originFileResolver.Resolve(tenantId), tenantId,
                                rtRecord.CkRecordId));
                            continue;
                        }

                        isInError |= SetDefaultValuesOnInsert(tenantId, ckRecordGraph!.AllAttributes.Values.ToList(), rtRecord,
                            originFileResolver,
                            operationResult, reference + $"/{attribute.AttributeName}");
                    }
                }
            }
        }

        return isInError;
    }
}