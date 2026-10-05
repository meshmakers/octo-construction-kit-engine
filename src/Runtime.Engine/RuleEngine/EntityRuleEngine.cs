using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Messages;
using Meshmakers.Octo.Runtime.Engine.Secrets;

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

            // AB#5532: explicit clearing of Secret attributes (clearSecretAttributes) becomes an explicit
            // null on the entity, which the Secret write step stores as "not set".
            var isInError = ApplyClearSecretAttributes(tenantId, ckTypeGraph, info, originFileResolver,
                operationResult);

            // check if all attributes are applied that are mandatory. If there is a mandatory attribute missing and no default value is set, throw an exception
            if (info.ModOption == EntityModOptions.Insert || info.ModOption == EntityModOptions.Replace)
            {
                if (info.RtEntity != null)
                {
                    // AB#5532: on replace a required secret without a value may still be carried over
                    // from the stored entity - the write step checks it once the stored value is known.
                    isInError |= SetDefaultValuesOnInsert(tenantId, ckTypeGraph.AllAttributes.Values.ToList(),
                        info.RtEntity, originFileResolver, operationResult,
                        $"{info.RtEntity.CkTypeId}@{info.RtEntity.RtId}",
                        deferSecretCheck: info.ModOption == EntityModOptions.Replace);
                }
            }
            else if (info.ModOption == EntityModOptions.Update)
            {
                foreach (var attribute in ckTypeGraph.AllAttributes.Values)
                {
                    if (!attribute.IsOptional && info.RtEntity != null &&
                        info.RtEntity.Attributes.TryGetValue(attribute.AttributeName, out var updatedValue) &&
                        (updatedValue == null || IsSecretPlaceholder(attribute, updatedValue)))
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

    /// <summary>
    ///     AB#5532 (concept §4.3): validates <see cref="IEntityUpdateInfo{TEntity}.ClearSecretAttributes" /> and
    ///     turns each entry into an explicit <c>null</c>. A name that is not a Secret attribute of the type,
    ///     a required secret, or a non-empty value for the same attribute is an error. Values are never
    ///     part of a message.
    /// </summary>
    private static bool ApplyClearSecretAttributes<TEntity>(string tenantId, CkTypeGraph ckTypeGraph,
        IEntityUpdateInfo<TEntity> info, IOriginFileResolver originFileResolver, OperationResult operationResult)
        where TEntity : RtEntity
    {
        var clearList = info.ClearSecretAttributes;
        if (clearList == null || clearList.Count == 0 || info.ModOption == EntityModOptions.Delete ||
            info.RtEntity == null)
        {
            return false;
        }

        var isInError = false;
        var rtId = (object?)info.RtId ?? info.RtEntity.RtId;
        foreach (var attributeName in clearList)
        {
            if (!ckTypeGraph.AllAttributesByName.TryGetValue(attributeName, out var attribute) ||
                attribute.ValueType != AttributeValueTypesDto.Secret)
            {
                operationResult.AddMessage(MessageCodes.SecretClearAttributeNotSecret(
                    originFileResolver.Resolve(tenantId), tenantId, attributeName, info.CkTypeId, rtId));
                isInError = true;
                continue;
            }

            if (!attribute.IsOptional)
            {
                operationResult.AddMessage(MessageCodes.SecretClearRequiredAttribute(
                    originFileResolver.Resolve(tenantId), tenantId, attribute.CkAttributeId, info.CkTypeId, rtId));
                isInError = true;
                continue;
            }

            if (info.RtEntity.Attributes.TryGetValue(attributeName, out var value) &&
                SecretWriteNormalizer.IsNonEmptyValue(value))
            {
                operationResult.AddMessage(MessageCodes.SecretSetAndCleared(
                    originFileResolver.Resolve(tenantId), tenantId, attribute.CkAttributeId, info.CkTypeId, rtId));
                isInError = true;
                continue;
            }

            info.RtEntity.SetAttributeRawValue(attributeName, null);
        }

        return isInError;
    }

    /// <summary>
    ///     A placeholder written to a Secret attribute is stored as "not set" (concept §3.6) - for a required
    ///     secret on update that is a clear. <c>""</c> is not a placeholder: it means "unchanged".
    /// </summary>
    private static bool IsSecretPlaceholder(CkTypeAttributeGraph attribute, object value)
    {
        return attribute.ValueType == AttributeValueTypesDto.Secret &&
               SecretWriteNormalizer.IsNotSetValue(value) && !IsEmptySecretInput(value);
    }

    private static bool IsEmptySecretInput(object value)
    {
        return value switch
        {
            string text => text.Length == 0,
            RtSecretValue { IsPending: true } secret => secret.RawValue.Length == 0,
            _ => false
        };
    }

    private bool SetDefaultValuesOnInsert(string tenantId, ICollection<CkTypeAttributeGraph> attributeGraphs,
        RtTypeWithAttributes rtType,
        IOriginFileResolver originFileResolver, OperationResult operationResult, string reference,
        bool deferSecretCheck)
    {
        var isInError = false;
        foreach (var attribute in attributeGraphs)
        {
            if (attribute.ValueType == AttributeValueTypesDto.Secret)
            {
                // AB#5532: a Secret attribute never takes a default (the compiler forbids them). Creating
                // requires a value for a required secret - "", a placeholder and null count as not set.
                // On replace the value may be carried over from the stored entity; the write step checks.
                if (!attribute.IsOptional && !deferSecretCheck &&
                    (!rtType.Attributes.TryGetValue(attribute.AttributeName, out var secretValue) ||
                     SecretWriteNormalizer.IsNotSetValue(secretValue)))
                {
                    operationResult.AddMessage(MessageCodes.MandatoryAttributeMissing(
                        originFileResolver.Resolve(tenantId), tenantId,
                        attribute.CkAttributeId, reference));
                    isInError = true;
                }

                continue;
            }

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
                                operationResult, reference + $"/{attribute.AttributeName}", deferSecretCheck);
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
                            operationResult, reference + $"/{attribute.AttributeName}", deferSecretCheck);
                    }
                }
            }
        }

        return isInError;
    }
}