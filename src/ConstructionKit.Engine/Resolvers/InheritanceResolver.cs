using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Implementation of <see cref="IInheritanceResolver"/> that resolves the inheritance of a compiled model.
/// </summary>
public class InheritanceResolver : IInheritanceResolver
{
    private readonly ILogger<InheritanceResolver> _logger;
    private readonly HashSet<CkId<CkTypeId>> _handledTypesHashSet;
    private readonly HashSet<CkId<CkRecordId>> _handledRecordHashSet;

    /// <summary>
    /// Creates a new instance of the <see cref="InheritanceResolver"/> class.
    /// </summary>
    /// <param name="logger"></param>
    public InheritanceResolver(ILogger<InheritanceResolver> logger)
    {
        _handledTypesHashSet = new HashSet<CkId<CkTypeId>>();
        _handledRecordHashSet = new HashSet<CkId<CkRecordId>>();
        _logger = logger;
    }

    /// <inheritdoc />
    public CkModelGraph Resolve(CkAggregatedModelElements aggregatedModelElements, CkModelGraph modelGraph, OperationResult operationResult)
    {
        _logger.LogInformation("Starting resolving inheritance");

        foreach (var ckTypeKeyValue in aggregatedModelElements.CkTypes)
        {
            var typeGraph = GetOrCreateTypeGraph(modelGraph, aggregatedModelElements, ckTypeKeyValue.Key, operationResult);
            GetDirectedAggregationsAndAttributes(modelGraph, aggregatedModelElements, ckTypeKeyValue.Value,
                typeGraph, operationResult);
        }

        foreach (var ckRecordKeyValue in aggregatedModelElements.CkRecords)
        {
            var recordGraph = GetOrCreateRecordGraph(modelGraph, aggregatedModelElements, ckRecordKeyValue.Key, operationResult);
            GetDirectedRecordAttributes(aggregatedModelElements, ckRecordKeyValue.Value,
                recordGraph, operationResult);
        }

        BuildInheritedAssociations(modelGraph, operationResult);

        _logger.LogInformation("Resolving inheritance completed");

        return modelGraph;
    }

    private CkTypeGraph GetOrCreateTypeGraph(CkModelGraph modelGraph,
        CkAggregatedModelElements aggregatedModelElements, CkId<CkTypeId> ckTypeId, OperationResult operationResult)
    {
        if (!aggregatedModelElements.CkTypes.TryGetValue(ckTypeId, out var ckType))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknown(ckTypeId));
            throw ModelValidationException.UnknownCkTypeId(ckTypeId);
        }

        if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
        {
            typeGraph = modelGraph.GetOrCreateType(ckTypeId, ckType);
        }

        if (!_handledTypesHashSet.Contains(ckTypeId))
        {
            var baseTypes = GetBaseTypes(aggregatedModelElements, ckTypeId, operationResult);
            typeGraph.AddBaseTypes(baseTypes);
            _handledTypesHashSet.Add(ckTypeId);
        }

        return typeGraph;
    }

    private CkRecordGraph GetOrCreateRecordGraph(CkModelGraph modelGraph,
        CkAggregatedModelElements aggregatedModelElements, CkId<CkRecordId> ckRecordId, OperationResult operationResult)
    {
        if (!aggregatedModelElements.CkRecords.TryGetValue(ckRecordId, out var ckRecord))
        {
            operationResult.AddMessage(MessageCodes.CkRecordIdUnknown(ckRecordId));
            throw ModelValidationException.UnknownCkRecordId(ckRecordId);
        }

        if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
        {
            recordGraph = modelGraph.GetOrCreateRecord(ckRecordId, ckRecord);
        }

        if (!_handledRecordHashSet.Contains(ckRecordId))
        {
            var baseTypes = GetBaseRecords(aggregatedModelElements, ckRecordId, operationResult);
            recordGraph.AddBaseRecords(baseTypes);
            _handledRecordHashSet.Add(ckRecordId);
        }

        return recordGraph;
    }

    private void GetDirectedAggregationsAndAttributes(CkModelGraph ckModelGraph,
        CkAggregatedModelElements aggregatedModelElements, CkTypeDto ckTypeDto,
        CkTypeGraph originTypeGraph, OperationResult operationResult)
    {
        for (int i = originTypeGraph.BaseTypes.Count - 1; i >= 0; i--)
        {
            var ckGraphTypeInheritance = originTypeGraph.BaseTypes.ElementAt(i);
            var baseCkType = aggregatedModelElements.CkTypes[ckGraphTypeInheritance.BaseCkTypeId];

            if (baseCkType.Attributes != null)
            {
                foreach (var typeAttribute in baseCkType.Attributes)
                {
                    originTypeGraph.Attributes.Add(typeAttribute);
                }
            }
        }

        // Add the current type's associations and attributes
        if (ckTypeDto.Associations != null)
        {
            foreach (var typeAssociation in ckTypeDto.Associations)
            {
                var targetCkTypeGraph = GetOrCreateTargetCkTypeGraph(ckModelGraph, aggregatedModelElements,
                    originTypeGraph, typeAssociation, operationResult);

                // Check if there is a duplicate association defined at the same type.
                if (originTypeGraph.Associations.Out.Owned.Any(x =>
                        x.CkRoleId == typeAssociation.CkRoleId && x.TargetCkTypeId == typeAssociation.TargetCkTypeId))
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdAssociationNotUnique(originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
                    continue;
                }

                // Check if there is the same association role defined in a base type with a target to the same target type inheritance chain
                var duplicateTypeAssociations = originTypeGraph.BaseTypes.SelectMany(i =>
                {
                    var baseCkTypeGraph = GetOrCreateTypeGraph(ckModelGraph, aggregatedModelElements,
                        i.BaseCkTypeId, operationResult);

                    return baseCkTypeGraph.Associations.Out.Owned.Where(x =>
                        x.CkRoleId == typeAssociation.CkRoleId && originTypeGraph.BaseTypes.Any(y =>
                            y.BaseCkTypeId == x.TargetCkTypeId)).Select(s => new { BaseCkTypeGraph = baseCkTypeGraph, s.TargetCkTypeId });
                }).ToList();

                if (duplicateTypeAssociations.Any())
                {
                    foreach (var duplicateTypeAssociation in duplicateTypeAssociations)
                    {
                        operationResult.AddMessage(MessageCodes.CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(
                            originTypeGraph.CkTypeId,
                            typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId,
                            duplicateTypeAssociation.BaseCkTypeGraph.CkTypeId, duplicateTypeAssociation.TargetCkTypeId));
                    }

                    continue;
                }
                
                // Check if there are target attributes defined and if they are valid
                if (typeAssociation.TargetAttributes != null)
                {
                    var invalidCkAttributeIds = typeAssociation.TargetAttributes.Where(a => 
                        targetCkTypeGraph.Attributes.All(b => b.CkAttributeId != a)).ToList();

                    invalidCkAttributeIds.ForEach(a =>
                    {
                        operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetAttributeIdForAssociation(ckTypeDto.TypeId,
                            typeAssociation.CkRoleId, a, typeAssociation.TargetCkTypeId));
                    });

                    if (invalidCkAttributeIds.Any())
                    {
                        continue;
                    }
                }

                targetCkTypeGraph.Associations.In.Owned.Add(typeAssociation);
                originTypeGraph.Associations.Out.Owned.Add(typeAssociation);
            }
        }

        if (ckTypeDto.Attributes != null)
        {
            var duplicateAttributeNames = ckTypeDto.Attributes.GroupBy(a => a.AttributeName).Where(a => a.Count() > 1).ToList();
            if (duplicateAttributeNames.Count > 0)
            {
                operationResult.AddMessage(
                    MessageCodes.CkTypeIdAttributeNameNotUnique(originTypeGraph.CkTypeId, duplicateAttributeNames.Select(a => a.Key)));
                throw ModelValidationException.DuplicateAttributeNamesInCkType(originTypeGraph.CkTypeId,
                    duplicateAttributeNames.Select(a => a.Key));
            }

            var duplicateAttributeIds = ckTypeDto.Attributes.GroupBy(a => a.CkAttributeId).Where(a => a.Count() > 1).ToList();
            if (duplicateAttributeIds.Count > 0)
            {
                operationResult.AddMessage(
                    MessageCodes.CkTypeIdAttributeIdNotUnique(originTypeGraph.CkTypeId, duplicateAttributeNames.Select(a => a.Key)));
                throw ModelValidationException.DuplicateAttributeIdsInCkType(originTypeGraph.CkTypeId,
                    duplicateAttributeIds.Select(a => a.Key));
            }

            foreach (var typeAttribute in ckTypeDto.Attributes)
            {
                if (originTypeGraph.Attributes.Any(a => a.CkAttributeId == typeAttribute.CkAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdAttributeIdNotUniqueByInheritance(originTypeGraph.CkTypeId, typeAttribute.CkAttributeId));
                    continue;
                }

                if (originTypeGraph.Attributes.Any(a =>
                        string.Compare(a.AttributeName, typeAttribute.AttributeName, StringComparison.OrdinalIgnoreCase) == 0))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdAttributeNameNotUniqueByInheritance(originTypeGraph.CkTypeId,
                            typeAttribute.AttributeName));
                    continue;
                }

                originTypeGraph.Attributes.Add(typeAttribute);
            }
        }
    }

    private void GetDirectedRecordAttributes(CkAggregatedModelElements aggregatedModelElements, CkRecordDto ckRecordDto,
        CkRecordGraph originRecordGraph, OperationResult operationResult)
    {
        for (int i = originRecordGraph.BaseRecords.Count - 1; i >= 0; i--)
        {
            var ckGraphRecordInheritance = originRecordGraph.BaseRecords.ElementAt(i);
            var baseCkRecord = aggregatedModelElements.CkRecords[ckGraphRecordInheritance.BaseCkRecordId];

            if (baseCkRecord.Attributes != null)
            {
                foreach (var typeAttribute in baseCkRecord.Attributes)
                {
                    originRecordGraph.Attributes.Add(typeAttribute);
                }
            }
        }

        if (ckRecordDto.Attributes != null)
        {
            var duplicateAttributeNames = ckRecordDto.Attributes.GroupBy(a => a.AttributeName).Where(a => a.Count() > 1).ToList();
            if (duplicateAttributeNames.Count > 0)
            {
                operationResult.AddMessage(
                    MessageCodes.CkRecordIdAttributeNameNotUnique(originRecordGraph.CkRecordId, duplicateAttributeNames.Select(a => a.Key)));
                throw ModelValidationException.DuplicateAttributeNamesInCkRecord(originRecordGraph.CkRecordId,
                    duplicateAttributeNames.Select(a => a.Key));
            }

            var duplicateAttributeIds = ckRecordDto.Attributes.GroupBy(a => a.CkAttributeId).Where(a => a.Count() > 1).ToList();
            if (duplicateAttributeIds.Count > 0)
            {
                operationResult.AddMessage(
                    MessageCodes.CkRecordIdAttributeIdNotUnique(originRecordGraph.CkRecordId, duplicateAttributeNames.Select(a => a.Key)));
                throw ModelValidationException.DuplicateAttributeIdsInCkRecord(originRecordGraph.CkRecordId,
                    duplicateAttributeIds.Select(a => a.Key));
            }

            foreach (var recordAttribute in ckRecordDto.Attributes)
            {
                if (originRecordGraph.Attributes.Any(a => a.CkAttributeId == recordAttribute.CkAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkRecordIdAttributeIdNotUniqueByInheritance(originRecordGraph.CkRecordId, recordAttribute.CkAttributeId));
                    continue;
                }

                if (originRecordGraph.Attributes.Any(a =>
                        string.Compare(a.AttributeName, recordAttribute.AttributeName, StringComparison.OrdinalIgnoreCase) == 0))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkRecordIdAttributeNameNotUniqueByInheritance(originRecordGraph.CkRecordId,
                            recordAttribute.AttributeName));
                    continue;
                }

                originRecordGraph.Attributes.Add(recordAttribute);
            }
        }
    }

    private CkTypeGraph GetOrCreateTargetCkTypeGraph(CkModelGraph ckModelGraph,
        CkAggregatedModelElements aggregatedModelElements, CkTypeGraph typeGraph,
        CkTypeAssociationDto typeAssociation, OperationResult operationResult)
    {
        if (!aggregatedModelElements.CkTypes.ContainsKey(typeAssociation.TargetCkTypeId))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetCkTypeIdForAssociation(typeGraph.CkTypeId,
                typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
            throw ModelValidationException.UnknownCkTypeIdForAssociationTarget(typeGraph.CkTypeId,
                typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId);
        }

        var targetCkTypeGraph = GetOrCreateTypeGraph(ckModelGraph, aggregatedModelElements,
            typeAssociation.TargetCkTypeId, operationResult);
        return targetCkTypeGraph;
    }

    private void BuildInheritedAssociations(CkModelGraph modelGraph, OperationResult operationResult)
    {
        var handledInheritanceHashSet = new HashSet<Tuple<CkId<CkTypeId>, CkId<CkTypeId>>>();
        foreach (var graphType in modelGraph.Types)
        {
            foreach (var ckGraphTypeInheritance in graphType.Value.BaseTypes.Reverse())
            {
                var baseGraphType = modelGraph.Types[ckGraphTypeInheritance.BaseCkTypeId];
                var inheritedGraphType = modelGraph.Types[ckGraphTypeInheritance.InheritorCkTypeId];

                // Ensure that we don't handle the same inheritance twice
                var tuple = new Tuple<CkId<CkTypeId>, CkId<CkTypeId>>(baseGraphType.CkTypeId,
                    inheritedGraphType.CkTypeId);
                if (handledInheritanceHashSet.Contains(tuple))
                {
                    continue;
                }

                handledInheritanceHashSet.Add(tuple);

                // Add the owned associations but also the inherited ones
                foreach (var typeAssociation in baseGraphType.Associations.In.Owned)
                {
                    inheritedGraphType.Associations.In.Inherited.Add(typeAssociation);
                }

                foreach (var typeAssociation in baseGraphType.Associations.In.Inherited)
                {
                    inheritedGraphType.Associations.In.Inherited.Add(typeAssociation);
                }

                foreach (var typeAssociation in baseGraphType.Associations.Out.Owned)
                {
                    if (inheritedGraphType.Associations.Out.Inherited.Any(x =>
                            x.CkRoleId == typeAssociation.CkRoleId && x.TargetCkTypeId == typeAssociation.TargetCkTypeId))
                    {
                        operationResult.AddMessage(MessageCodes.CkTypeIdOutAssociationNotUniqueByInheritance(inheritedGraphType.CkTypeId,
                            typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
                        continue;
                    }

                    inheritedGraphType.Associations.Out.Inherited.Add(typeAssociation);
                }

                foreach (var typeAssociation in baseGraphType.Associations.Out.Inherited)
                {
                    inheritedGraphType.Associations.Out.Inherited.Add(typeAssociation);
                }
            }
        }
    }

    private static IList<CkGraphTypeInheritance> GetBaseTypes(CkAggregatedModelElements aggregatedModelElements,
        CkId<CkTypeId> ckTypeId, OperationResult operationResult)
    {
        var ckTypeIds = new List<CkGraphTypeInheritance>();

        int i = 0;
        CkId<CkTypeId>? currentCkTypeId = ckTypeId;
        CkId<CkTypeId>? lastCkTypeId = ckTypeId;
        while (currentCkTypeId != null &&
               aggregatedModelElements.CkTypes.TryGetValue(currentCkTypeId.Value, out var currentCkType))
        {
            var baseCkTypeId = currentCkType.DerivedFromCkTypeId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(MessageCodes.DerivedFromCkTypeIdThatIsFinal(currentCkTypeId.Value, lastCkTypeId.Value));
                    throw ModelValidationException.DerivedFromCkTypeIdThatIsFinal(currentCkTypeId.Value, lastCkTypeId.Value);
                }
            }

            if (baseCkTypeId.HasValue)
            {
                ckTypeIds.Add(new CkGraphTypeInheritance(currentCkTypeId.Value, baseCkTypeId.Value, i++));
            }

            lastCkTypeId = currentCkTypeId;
            currentCkTypeId = baseCkTypeId;
        }

        if (currentCkTypeId != null)
        {
            operationResult.AddMessage(MessageCodes.UnknownCkTypeIdForInheritance(currentCkTypeId.Value));
            throw ModelValidationException.UnknownCkTypeIdForInheritance(currentCkTypeId.Value);
        }

        if (!ckTypeIds.Any())
        {
            if (!CompilerStatics.WhiteListedCkTypeIds.Any(x => x.ModelId.ModelId == ckTypeId.ModelId.ModelId
                                                               && x.Key.TypeId == ckTypeId.Key.TypeId))
            {
                operationResult.AddMessage(
                    MessageCodes.InheritanceMissing(ckTypeId.Key.TypeId));
                throw ModelValidationException.InheritanceMissing(ckTypeId.Key.TypeId);
            }
        }

        return ckTypeIds;
    }

    private static IList<CkGraphRecordInheritance> GetBaseRecords(CkAggregatedModelElements aggregatedModelElements,
        CkId<CkRecordId> ckRecordId, OperationResult operationResult)
    {
        var ckRecordIds = new List<CkGraphRecordInheritance>();

        int i = 0;
        CkId<CkRecordId>? currentCkRecordId = ckRecordId;
        CkId<CkRecordId>? lastCkRecordId = ckRecordId;
        while (currentCkRecordId != null &&
               aggregatedModelElements.CkRecords.TryGetValue(currentCkRecordId.Value, out var currentCkType))
        {
            var baseCkRecordId = currentCkType.DerivedFromCkRecordId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(
                        MessageCodes.DerivedFromCkRecordIdThatIsFinal(currentCkRecordId.Value, lastCkRecordId.Value));
                    throw ModelValidationException.DerivedFromCkRecordIdThatIsFinal(currentCkRecordId.Value, lastCkRecordId.Value);
                }
            }

            if (baseCkRecordId.HasValue)
            {
                ckRecordIds.Add(new CkGraphRecordInheritance(currentCkRecordId.Value, baseCkRecordId.Value, i++));
            }

            lastCkRecordId = currentCkRecordId;
            currentCkRecordId = baseCkRecordId;
        }

        if (currentCkRecordId != null)
        {
            operationResult.AddMessage(MessageCodes.UnknownCkRecordIdForInheritance(currentCkRecordId.Value));
            throw ModelValidationException.UnknownCkRecordIdForInheritance(currentCkRecordId.Value);
        }

        return ckRecordIds;
    }
}