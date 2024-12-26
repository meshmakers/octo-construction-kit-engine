using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Implementation of <see cref="IInheritanceResolver" /> that resolves the inheritance of a compiled model.
/// </summary>
internal class InheritanceResolver : IInheritanceResolver
{
    private readonly ILogger<InheritanceResolver> _logger;

    /// <summary>
    ///     Creates a new instance of the <see cref="InheritanceResolver" /> class.
    /// </summary>
    /// <param name="logger"></param>
    public InheritanceResolver(ILogger<InheritanceResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public CkModelGraph Resolve(CkModelGraph modelGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        _logger.LogDebug("Starting resolving inheritance");

        HashSet<CkId<CkRecordId>> handledRecordHashSet = new();
        HashSet<CkId<CkTypeId>> handledTypesHashSet = new();

        foreach (var ckTypeKeyValue in modelGraph.Types)
        {
            _logger.LogDebug("Resolving inheritance for type {CkTypeId}", ckTypeKeyValue.Key);
            GetAndUpdateTypeGraph(handledTypesHashSet, modelGraph, ckTypeKeyValue.Key, originFileResolver, operationResult);
            GetDirectedAggregationsAndAttributes(handledTypesHashSet, modelGraph, ckTypeKeyValue.Value, originFileResolver,
                operationResult);
        }

        foreach (var ckRecordKeyValue in modelGraph.Records)
        {
            _logger.LogDebug("Resolving inheritance for record {CkRecordId}", ckRecordKeyValue.Key);
            var recordGraph = GetAndUpdateRecordGraph(handledRecordHashSet, modelGraph, ckRecordKeyValue.Key, originFileResolver,
                operationResult);
            GetDirectedRecordAttributes(modelGraph, recordGraph, originFileResolver, operationResult);
        }

        _logger.LogDebug("Resolving dependencies based on inheritance");
        BuildInheritedConfiguration(modelGraph, originFileResolver, operationResult);

        _logger.LogDebug("Resolving inheritance completed");

        return modelGraph;
    }

    private CkTypeGraph GetAndUpdateTypeGraph(HashSet<CkId<CkTypeId>> handledTypesHashSet, CkModelGraph modelGraph, CkId<CkTypeId> ckTypeId,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknown(originFileResolver.Resolve(ckTypeId), ckTypeId));
            throw ModelValidationException.UnknownCkTypeId(ckTypeId);
        }


        if (!handledTypesHashSet.Contains(ckTypeId))
        {
            var baseTypes = GetBaseTypes(modelGraph, ckTypeId, originFileResolver, operationResult);
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
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtType(originFileResolver.Resolve(ckTypeId),
                        ckTypeAttribute.CkAttributeId, ckTypeId));
                    continue;
                }

                typeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            handledTypesHashSet.Add(ckTypeId);
        }

        return typeGraph;
    }

    private CkRecordGraph GetAndUpdateRecordGraph(HashSet<CkId<CkRecordId>> handledRecordHashSet, CkModelGraph modelGraph,
        CkId<CkRecordId> ckRecordId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
        {
            operationResult.AddMessage(MessageCodes.CkRecordIdUnknown(originFileResolver.Resolve(ckRecordId), ckRecordId));
            throw ModelValidationException.UnknownCkRecordId(ckRecordId);
        }

        if (!handledRecordHashSet.Contains(ckRecordId))
        {
            var baseTypes = GetBaseRecords(modelGraph, ckRecordId, originFileResolver, operationResult);
            recordGraph.AddBaseRecords(baseTypes);

            foreach (var ckTypeAttribute in recordGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(ckTypeAttribute.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtRecord(originFileResolver.Resolve(ckRecordId),
                        ckTypeAttribute.CkAttributeId, ckRecordId));
                    continue;
                }

                recordGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            handledRecordHashSet.Add(ckRecordId);
        }

        return recordGraph;
    }

    private void GetDirectedAggregationsAndAttributes(HashSet<CkId<CkTypeId>> handledTypesHashSet, CkModelGraph ckModelGraph,
        CkTypeGraph originTypeGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        _logger.LogDebug("Resolving directed aggregations and attributes for type {CkTypeId}", originTypeGraph.CkTypeId);
        for (var i = originTypeGraph.BaseTypes.Count - 1; i >= 0; i--)
        {
            var ckGraphTypeInheritance = originTypeGraph.BaseTypes.ElementAt(i);
            var baseCkType = ckModelGraph.Types[ckGraphTypeInheritance.BaseCkTypeId];


            foreach (var typeAttribute in baseCkType.DefinedAttributes)
            {
                var ckTypeAttributeGraph = baseCkType.AllAttributes[typeAttribute.CkAttributeId];
                // Here is checked if the attribute id already exists on the type (e. g. defined at type or inherited from another base type)
                if (!originTypeGraph.TryAddAttribute(ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdAttributeIdNotUniqueByInheritance(originFileResolver.Resolve(originTypeGraph.CkTypeId),
                            baseCkType.CkTypeId, typeAttribute.CkAttributeId,
                            originTypeGraph.CkTypeId));
                }
                originTypeGraph.IsStreamType |= ckTypeAttributeGraph.IsDataStream;
            }
        }

        // Add the current type's associations and attributes
        foreach (var typeAssociation in originTypeGraph.Associations.DefinedAssociations)
        {
            // Check if the association role exists - ckModelGraph already contains all association roles
            if (!ckModelGraph.AssociationRoles.TryGetValue(typeAssociation.CkRoleId, out var ckAssociationRole))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationRoleIdUnknown(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId, typeAssociation.CkRoleId));
                continue;
            }

            var targetCkTypeGraph =
                GetAndUpdateTargetCkTypeGraph(handledTypesHashSet, ckModelGraph, originTypeGraph, typeAssociation, originFileResolver,
                    operationResult);

            // Check if there is a duplicate association defined at the same type.
            if (originTypeGraph.Associations.Out.Owned.Any(x =>
                    x.CkRoleId == typeAssociation.CkRoleId && x.TargetCkTypeId == typeAssociation.TargetCkTypeId))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationNotUnique(originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId, typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
                continue;
            }

            // Check if there is the same association role defined in a base type with a target to the same target type inheritance chain
            var duplicateTypeAssociations = originTypeGraph.BaseTypes.SelectMany(i =>
            {
                var baseCkTypeGraph = GetAndUpdateTypeGraph(handledTypesHashSet, ckModelGraph, i.BaseCkTypeId, originFileResolver,
                    operationResult);

                return baseCkTypeGraph.Associations.Out.Owned.Where(x =>
                    x.CkRoleId == typeAssociation.CkRoleId && originTypeGraph.BaseTypes.Any(y =>
                        y.BaseCkTypeId == x.TargetCkTypeId)).Select(s => new { BaseCkTypeGraph = baseCkTypeGraph, s.TargetCkTypeId });
            }).ToList();

            if (duplicateTypeAssociations.Any())
            {
                foreach (var duplicateTypeAssociation in duplicateTypeAssociations)
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(
                        originFileResolver.Resolve(originTypeGraph.CkTypeId),
                        originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId,
                        duplicateTypeAssociation.BaseCkTypeGraph.CkTypeId, duplicateTypeAssociation.TargetCkTypeId));
                }

                continue;
            }

            // Check if there are target attributes defined and if they are valid
            if (typeAssociation.TargetCkAttributeIds != null)
            {
                var invalidCkAttributeIds = typeAssociation.TargetCkAttributeIds.Where(a =>
                    targetCkTypeGraph.AllAttributes.All(b => b.Key != a)).ToList();

                invalidCkAttributeIds.ForEach(a =>
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetAttributeIdForAssociation(
                        originFileResolver.Resolve(originTypeGraph.CkTypeId), originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, a, typeAssociation.TargetCkTypeId));
                });

                if (invalidCkAttributeIds.Any())
                {
                    continue;
                }
            }

            var inboundAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.InboundName,
                ckAssociationRole.InboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            var outboundAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.OutboundName,
                ckAssociationRole.OutboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            targetCkTypeGraph.Associations.In.Owned.Add(inboundAssociationGraph);
            originTypeGraph.Associations.Out.Owned.Add(outboundAssociationGraph);
        }

        // Check if the attributes (=defined+inherited at type) have duplicate attribute names
        var duplicateAttributeNames = originTypeGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkTypeIdAttributeNameNotUniqueByInheritance(originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId,
                    string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    private void GetDirectedRecordAttributes(CkModelGraph modelGraph,
        CkRecordGraph originRecordGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        for (var i = originRecordGraph.BaseRecords.Count - 1; i >= 0; i--)
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
                        MessageCodes.CkRecordIdAttributeIdNotUniqueByInheritance(originFileResolver.Resolve(baseCkRecord.CkRecordId),
                            baseCkRecord.CkRecordId, typeAttribute.CkAttributeId, originRecordGraph.CkRecordId));
                }
            }
        }

        // Check if the attributes (=defined+inherited at record) have duplicate attribute names
        var duplicateAttributeNames = originRecordGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkRecordIdAttributeNameNotUniqueByInheritance(originFileResolver.Resolve(originRecordGraph.CkRecordId),
                    originRecordGraph.CkRecordId,
                    string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    private CkTypeGraph GetAndUpdateTargetCkTypeGraph(HashSet<CkId<CkTypeId>> handledTypesHashSet, CkModelGraph ckModelGraph,
        CkTypeGraph typeGraph,
        CkTypeAssociationDto typeAssociation, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        if (!ckModelGraph.Types.ContainsKey(typeAssociation.TargetCkTypeId))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetCkTypeIdForAssociation(
                originFileResolver.Resolve(typeGraph.CkTypeId),
                typeGraph.CkTypeId, typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
            throw ModelValidationException.UnknownCkTypeIdForAssociationTarget(typeGraph.CkTypeId,
                typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId);
        }

        var targetCkTypeGraph = GetAndUpdateTypeGraph(handledTypesHashSet, ckModelGraph, typeAssociation.TargetCkTypeId, originFileResolver,
            operationResult);
        return targetCkTypeGraph;
    }

    private void BuildInheritedConfiguration(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
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

                // Set the defining collection type id and merge index fields.
                if (baseGraphType.IsCollectionRoot)
                {
                    graphType.Value.SetDefiningCollectionCkTypeId(baseGraphType.CkTypeId);

                    baseGraphType.MergeTextIndexes(graphType.Value.Indexes);
                }

                // Ensure that we don't handle the same inheritance twice
                var tuple = new Tuple<CkId<CkTypeId>, CkId<CkTypeId>>(baseGraphType.CkTypeId,
                    inheritedGraphType.CkTypeId);
                // ReSharper disable once CanSimplifySetAddingWithSingleCall
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
                        operationResult.AddMessage(MessageCodes.CkTypeIdOutAssociationNotUniqueByInheritance(
                            originFileResolver.Resolve(inheritedGraphType.CkTypeId),
                            inheritedGraphType.CkTypeId,
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
        CkId<CkTypeId> ckTypeId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var ckTypeIds = new List<CkGraphTypeInheritance>();

        var i = 0;
        CkId<CkTypeId>? currentCkTypeId = ckTypeId;
        CkId<CkTypeId> lastCkTypeId = ckTypeId;
        while (currentCkTypeId != null &&
               modelGraph.Types.TryGetValue(currentCkTypeId, out var currentCkType))
        {
            var baseCkTypeId = currentCkType.DerivedFromCkTypeId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(MessageCodes.DerivedFromCkTypeIdThatIsFinal(originFileResolver.Resolve(ckTypeId),
                        currentCkTypeId, lastCkTypeId));
                    throw ModelValidationException.DerivedFromCkTypeIdThatIsFinal(currentCkTypeId, lastCkTypeId);
                }
            }

            if (baseCkTypeId != null)
            {
                ckTypeIds.Add(new CkGraphTypeInheritance(currentCkTypeId, baseCkTypeId, i++));
            }

            lastCkTypeId = currentCkTypeId;
            currentCkTypeId = baseCkTypeId;
        }

        if (currentCkTypeId != null)
        {
            operationResult.AddMessage(MessageCodes.UnknownCkTypeIdForInheritance(originFileResolver.Resolve(ckTypeId), currentCkTypeId));
            throw ModelValidationException.UnknownCkTypeIdForInheritance(currentCkTypeId);
        }

        if (!ckTypeIds.Any())
        {
            if (!CompilerStatics.WhiteListedCkTypeIds.Any(x => x.ModelId.ModelId == ckTypeId.ModelId.ModelId
                                                               && x.Key.TypeId == ckTypeId.Key.TypeId))
            {
                operationResult.AddMessage(
                    MessageCodes.InheritanceMissing(originFileResolver.Resolve(ckTypeId), ckTypeId.Key.TypeId));
                throw ModelValidationException.InheritanceMissing(ckTypeId.Key.TypeId);
            }
        }

        return ckTypeIds;
    }

    private static IList<CkGraphRecordInheritance> GetBaseRecords(CkModelGraph modelGraph,
        CkId<CkRecordId> ckRecordId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var ckRecordIds = new List<CkGraphRecordInheritance>();

        var i = 0;
        CkId<CkRecordId>? currentCkRecordId = ckRecordId;
        CkId<CkRecordId> lastCkRecordId = ckRecordId;
        while (currentCkRecordId != null &&
               modelGraph.Records.TryGetValue(currentCkRecordId, out var currentCkType))
        {
            var baseCkRecordId = currentCkType.DerivedFromCkRecordId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(
                        MessageCodes.DerivedFromCkRecordIdThatIsFinal(originFileResolver.Resolve(ckRecordId),
                            currentCkRecordId, lastCkRecordId));
                    throw ModelValidationException.DerivedFromCkRecordIdThatIsFinal(currentCkRecordId, lastCkRecordId);
                }
            }

            if (baseCkRecordId != null)
            {
                ckRecordIds.Add(new CkGraphRecordInheritance(currentCkRecordId, baseCkRecordId, i++));
            }

            lastCkRecordId = currentCkRecordId;
            currentCkRecordId = baseCkRecordId;
        }

        if (currentCkRecordId != null)
        {
            operationResult.AddMessage(
                MessageCodes.UnknownCkRecordIdForInheritance(originFileResolver.Resolve(ckRecordId), currentCkRecordId));
            throw ModelValidationException.UnknownCkRecordIdForInheritance(currentCkRecordId);
        }

        return ckRecordIds;
    }
}