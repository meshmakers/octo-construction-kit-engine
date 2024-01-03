using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Implementation of <see cref="IInheritanceResolver"/> that resolves the inheritance of a compiled model.
/// </summary>
internal class InheritanceResolver : IInheritanceResolver
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
    public CkModelGraph Resolve(CkModelGraph modelGraph, OperationResult operationResult)
    {
        _logger.LogInformation("Starting resolving inheritance");

        foreach (var ckTypeKeyValue in modelGraph.Types)
        {
            _logger.LogDebug("Resolving inheritance for type {CkTypeId}", ckTypeKeyValue.Key);
            GetAndUpdateTypeGraph(modelGraph, ckTypeKeyValue.Key, operationResult);
            GetDirectedAggregationsAndAttributes(modelGraph, ckTypeKeyValue.Value, operationResult);
        }

        foreach (var ckRecordKeyValue in modelGraph.Records)
        {
            _logger.LogDebug("Resolving inheritance for record {CkRecordId}", ckRecordKeyValue.Key);
            var recordGraph = GetAndUpdateRecordGraph(modelGraph, ckRecordKeyValue.Key, operationResult);
            GetDirectedRecordAttributes(modelGraph, recordGraph, operationResult);
        }

        _logger.LogDebug("Resolving dependencies based on inheritance");
        BuildInheritedAssociations(modelGraph, operationResult);

        _logger.LogInformation("Resolving inheritance completed");

        return modelGraph;
    }

    private CkTypeGraph GetAndUpdateTypeGraph(CkModelGraph modelGraph, CkId<CkTypeId> ckTypeId, OperationResult operationResult)
    {
        if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknown(ckTypeId));
            throw ModelValidationException.UnknownCkTypeId(ckTypeId);
        }


        if (!_handledTypesHashSet.Contains(ckTypeId))
        {
            var baseTypes = GetBaseTypes(modelGraph, ckTypeId, operationResult);
            typeGraph.AddBaseTypes(baseTypes);

            if (baseTypes.Any() && baseTypes.All(t => CompilerStatics.WhiteListedCkTypeIds.Contains(t.BaseCkTypeId)))
            {
                typeGraph.SetIsCollectionRoot(true);
                typeGraph.SetDefiningCollectionCkTypeId(typeGraph.CkTypeId);
            }

            foreach (var ckTypeAttribute in typeGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(ckTypeAttribute.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtType(ckTypeAttribute.CkAttributeId, ckTypeId));
                    continue;
                }
                typeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            _handledTypesHashSet.Add(ckTypeId);
        }

        return typeGraph;
    }

    private CkRecordGraph GetAndUpdateRecordGraph(CkModelGraph modelGraph, CkId<CkRecordId> ckRecordId, OperationResult operationResult)
    {
        if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
        {
            operationResult.AddMessage(MessageCodes.CkRecordIdUnknown(ckRecordId));
            throw ModelValidationException.UnknownCkRecordId(ckRecordId);
        }

        if (!_handledRecordHashSet.Contains(ckRecordId))
        {
            var baseTypes = GetBaseRecords(modelGraph, ckRecordId, operationResult);
            recordGraph.AddBaseRecords(baseTypes);
            
            foreach (var ckTypeAttribute in recordGraph.DefinedAttributes)
            {
                recordGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    modelGraph.Attributes[ckTypeAttribute.CkAttributeId]));
            }
            
            _handledRecordHashSet.Add(ckRecordId);
        }

        return recordGraph;
    }

    private void GetDirectedAggregationsAndAttributes(CkModelGraph ckModelGraph,
        CkTypeGraph originTypeGraph, OperationResult operationResult)
    {
        _logger.LogDebug("Resolving directed aggregations and attributes for type {CkTypeId}", originTypeGraph.CkTypeId);
        for (int i = originTypeGraph.BaseTypes.Count - 1; i >= 0; i--)
        {
            var ckGraphTypeInheritance = originTypeGraph.BaseTypes.ElementAt(i);
            var baseCkType = ckModelGraph.Types[ckGraphTypeInheritance.BaseCkTypeId];
            if (baseCkType.IsCollectionRoot)
            {
                originTypeGraph.SetDefiningCollectionCkTypeId(baseCkType.CkTypeId);
            }

            foreach (var typeAttribute in baseCkType.DefinedAttributes)
            {
                var ckTypeAttributeGraph = baseCkType.AllAttributes[typeAttribute.CkAttributeId];
                // Here is checked if the attribute id already exists on the type (e. g. defined at type or inherited from another base type)
                if (!originTypeGraph.TryAddAttribute(ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdAttributeIdNotUniqueByInheritance(baseCkType.CkTypeId, typeAttribute.CkAttributeId, originTypeGraph.CkTypeId));
                }
            }
        }

        // Add the current type's associations and attributes
        foreach (var typeAssociation in originTypeGraph.Associations.DefinedAssociations)
        {
            // Check if the association role exists - ckModelGraph already contains all association roles
            if (!ckModelGraph.AssociationRoles.TryGetValue(typeAssociation.CkRoleId, out var ckAssociationRole))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationRoleIdUnknown(originTypeGraph.CkTypeId,
                    typeAssociation.CkRoleId));
                continue;
            }

            var targetCkTypeGraph = GetAndUpdateTargetCkTypeGraph(ckModelGraph, originTypeGraph, typeAssociation, operationResult);

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
                var baseCkTypeGraph = GetAndUpdateTypeGraph(ckModelGraph, i.BaseCkTypeId, operationResult);

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
                    targetCkTypeGraph.AllAttributes.All(b => b.Key != a)).ToList();

                invalidCkAttributeIds.ForEach(a =>
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetAttributeIdForAssociation(originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, a, typeAssociation.TargetCkTypeId));
                });

                if (invalidCkAttributeIds.Any())
                {
                    continue;
                }
            }

            var inAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.OutboundName,
                ckAssociationRole.OutboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            var outAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.InboundName,
                ckAssociationRole.InboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            targetCkTypeGraph.Associations.In.Owned.Add(inAssociationGraph);
            originTypeGraph.Associations.Out.Owned.Add(outAssociationGraph);
        }
        
        // Check if the attributes (=defined+inherited at type) have duplicate attribute names
        var duplicateAttributeNames = originTypeGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkTypeIdAttributeNameNotUniqueByInheritance(originTypeGraph.CkTypeId, string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    private void GetDirectedRecordAttributes(CkModelGraph modelGraph,
        CkRecordGraph originRecordGraph, OperationResult operationResult)
    {
        for (int i = originRecordGraph.BaseRecords.Count - 1; i >= 0; i--)
        {
            var ckGraphRecordInheritance = originRecordGraph.BaseRecords.ElementAt(i);
            var baseCkRecord = modelGraph.Records[ckGraphRecordInheritance.BaseCkRecordId];

            foreach (var typeAttribute in baseCkRecord.DefinedAttributes)
            {
                // Here is checked if the attribute id already exists on the record (e. g. defined at record or inherited from another base record)
                var ckTypeAttributeGraph = baseCkRecord.AllAttributes[typeAttribute.CkAttributeId];
                if (!originRecordGraph.TryAddAttribute(ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkRecordIdAttributeIdNotUniqueByInheritance(baseCkRecord.CkRecordId, typeAttribute.CkAttributeId, originRecordGraph.CkRecordId));
                }
            }
        }

        // Check if the attributes (=defined+inherited at record) have duplicate attribute names
        var duplicateAttributeNames = originRecordGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkRecordIdAttributeNameNotUniqueByInheritance(originRecordGraph.CkRecordId, string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    private CkTypeGraph GetAndUpdateTargetCkTypeGraph(CkModelGraph ckModelGraph, CkTypeGraph typeGraph,
        CkTypeAssociationDto typeAssociation, OperationResult operationResult)
    {
        if (!ckModelGraph.Types.ContainsKey(typeAssociation.TargetCkTypeId))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetCkTypeIdForAssociation(typeGraph.CkTypeId,
                typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
            throw ModelValidationException.UnknownCkTypeIdForAssociationTarget(typeGraph.CkTypeId,
                typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId);
        }

        var targetCkTypeGraph = GetAndUpdateTypeGraph(ckModelGraph, typeAssociation.TargetCkTypeId, operationResult);
        return targetCkTypeGraph;
    }

    private void BuildInheritedAssociations(CkModelGraph modelGraph, OperationResult operationResult)
    {
        var handledInheritanceHashSet = new HashSet<Tuple<CkId<CkTypeId>, CkId<CkTypeId>>>();
        foreach (var graphType in modelGraph.Types)
        {
            List<CkTypeGraph> baseList = new();
            foreach (var ckGraphTypeInheritance in graphType.Value.BaseTypes.Reverse())
            {
                var baseGraphType = modelGraph.Types[ckGraphTypeInheritance.BaseCkTypeId];
                var inheritedGraphType = modelGraph.Types[ckGraphTypeInheritance.InheritorCkTypeId];
                baseList.Add(baseGraphType);

                // Ensure that we don't handle the same inheritance twice
                var tuple = new Tuple<CkId<CkTypeId>, CkId<CkTypeId>>(baseGraphType.CkTypeId,
                    inheritedGraphType.CkTypeId);
                if (handledInheritanceHashSet.Contains(tuple))
                {
                    continue;
                }

                handledInheritanceHashSet.Add(tuple);
                baseList.ForEach(b => b.AddDerivedTypes(ckGraphTypeInheritance));

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

    private static IList<CkGraphTypeInheritance> GetBaseTypes(CkModelGraph modelGraph,
        CkId<CkTypeId> ckTypeId, OperationResult operationResult)
    {
        var ckTypeIds = new List<CkGraphTypeInheritance>();

        int i = 0;
        CkId<CkTypeId>? currentCkTypeId = ckTypeId;
        CkId<CkTypeId>? lastCkTypeId = ckTypeId;
        while (currentCkTypeId != null &&
               modelGraph.Types.TryGetValue(currentCkTypeId.Value, out var currentCkType))
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

    private static IList<CkGraphRecordInheritance> GetBaseRecords(CkModelGraph modelGraph,
        CkId<CkRecordId> ckRecordId, OperationResult operationResult)
    {
        var ckRecordIds = new List<CkGraphRecordInheritance>();

        int i = 0;
        CkId<CkRecordId>? currentCkRecordId = ckRecordId;
        CkId<CkRecordId>? lastCkRecordId = ckRecordId;
        while (currentCkRecordId != null &&
               modelGraph.Records.TryGetValue(currentCkRecordId.Value, out var currentCkType))
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