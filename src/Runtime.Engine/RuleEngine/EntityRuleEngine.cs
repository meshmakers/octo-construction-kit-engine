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
    public async Task<EntityRuleEngineResult<TEntity>> ValidateAsync<TEntity>(string tenantId,
        IReadOnlyList<IEntityUpdateInfo<TEntity>> entityUpdateInfos, IOriginFileResolver originFileResolver,
        OperationResult operationResult) where TEntity : RtEntity
    {
        var entitiesToCreate = new ConcurrentBag<TEntity>();
        var entitiesToUpdate = new ConcurrentDictionary<RtEntityId, TEntity>();
        var entitiesToReplace = new ConcurrentDictionary<RtEntityId, TEntity>();
        var entitiesToDelete = new ConcurrentBag<RtEntityId>();
        var updateGuards = new ConcurrentDictionary<RtEntityId, AttributeNewerThanGuard>();

        await Parallel.ForEachAsync(entityUpdateInfos, (info, token) =>
        {
            if (!ckCache.TryGetRtCkType(tenantId, info.CkTypeId, out var ckTypeGraph))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdNotFound(originFileResolver.Resolve(tenantId), tenantId,
                    info.CkTypeId));
                return ValueTask.CompletedTask;
            }

            if (ckTypeGraph.IsAbstract)
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdIsAbstract(originFileResolver.Resolve(tenantId),
                    tenantId,
                    info.CkTypeId));
                return ValueTask.CompletedTask;
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
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtInsert(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.CkTypeId));
                        return ValueTask.CompletedTask;
                    }

                    entitiesToCreate.Add(info.RtEntity);
                    break;
                case EntityModOptions.Update:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtUpdateReplace(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.CkTypeId, info.RtId ?? throw PersistenceException.RtIdNotSet()));
                        return ValueTask.CompletedTask;
                    }

                    if (!entitiesToUpdate.TryAdd(info.GetRtEntityId(), info.RtEntity))
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityIdAlreadyExistInUpdateList(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.CkTypeId, info.RtId ?? throw PersistenceException.RtIdNotSet()));
                        return ValueTask.CompletedTask;
                    }

                    if (info.UpdateGuard != null)
                    {
                        updateGuards.TryAdd(info.GetRtEntityId(), info.UpdateGuard);
                    }

                    break;
                case EntityModOptions.Replace:
                    if (info.RtEntity == null)
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityNeedsToBeDefinedAtUpdateReplace(
                            originFileResolver.Resolve(tenantId), tenantId,
                            info.CkTypeId, info.RtId ?? throw PersistenceException.RtIdNotSet()));
                        return ValueTask.CompletedTask;
                    }

                    if (!entitiesToReplace.TryAdd(info.GetRtEntityId(), info.RtEntity))
                    {
                        operationResult.AddMessage(MessageCodes.RtEntityIdAlreadyExistInUpdateList(
                            originFileResolver.Resolve(tenantId),
                            tenantId,
                            info.CkTypeId, info.RtId ?? throw PersistenceException.RtIdNotSet()));
                        return ValueTask.CompletedTask;
                    }

                    break;
                case EntityModOptions.Delete:
                    entitiesToDelete.Add(info.GetRtEntityId());
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
                entitiesToDelete.ToList(),
                updateGuards.ToDictionary(k => k.Key, v => v.Value));

        return entityValidatorResult;
    }

    private bool SetDefaultValuesOnInsert(string tenantId, ICollection<CkTypeAttributeGraph> attributeGraphs,
        RtTypeWithAttributes rtType,
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
                            rtType.SetAttributeValue(attribute.AttributeName, attribute.ValueType,
                                attribute.DefaultValues);
                            break;
                        default:
                            rtType.SetAttributeValue(attribute.AttributeName, attribute.ValueType,
                                attribute.DefaultValues.FirstOrDefault());
                            break;
                    }
                }
                // if no value set, at least there must be an auto increment reference;
                // otherwise the attribute is missing
                else if (string.IsNullOrWhiteSpace(attribute.AutoIncrementReference))
                {
                    operationResult.AddMessage(MessageCodes.MandatoryAttributeMissing(
                        originFileResolver.Resolve(tenantId), tenantId,
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
                            if (!ckCache.TryGetRtCkRecord(tenantId, rtRecord.CkRecordId, out var ckRecordGraph) ||
                                ckRecordGraph == null)
                            {
                                operationResult.AddMessage(MessageCodes.CkRecordIdNotFound(
                                    originFileResolver.Resolve(tenantId), tenantId,
                                    rtRecord.CkRecordId));
                                continue;
                            }

                            isInError |= SetDefaultValuesOnInsert(tenantId, ckRecordGraph.AllAttributes.Values.ToList(),
                                rtRecord,
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
                        if (!ckCache.TryGetRtCkRecord(tenantId, rtRecord.CkRecordId, out var ckRecordGraph) ||
                            ckRecordGraph == null)
                        {
                            operationResult.AddMessage(MessageCodes.CkRecordIdNotFound(
                                originFileResolver.Resolve(tenantId), tenantId,
                                rtRecord.CkRecordId));
                            continue;
                        }

                        isInError |= SetDefaultValuesOnInsert(tenantId, ckRecordGraph.AllAttributes.Values.ToList(),
                            rtRecord,
                            originFileResolver,
                            operationResult, reference + $"/{attribute.AttributeName}");
                    }
                }
            }
        }

        return isInError;
    }
}