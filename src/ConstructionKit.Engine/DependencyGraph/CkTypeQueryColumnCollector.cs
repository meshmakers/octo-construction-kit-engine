using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

internal class CkTypeQueryColumnCollector(CkModelGraph ckModelGraph)
{
    private const string Array = "[{0}]";
    private const string FirstElement = "0";
    private const string AllElements = "*";
    private const string Separator = ".";
    private const string NavigationSeparator = "->";
    private const string AssociationMetaSeparator = "::";
    private const string SystemAttributeRtId = "RtId";
    private const string SystemAttributeRtWellKnownName = "RtWellKnownName";
    private const string SystemAttributeRtVersion = "RtVersion";
    private const string SystemAttributeRtCreationDateTime = "RtCreationDateTime";
    private const string SystemAttributeRtChangedDateTime = "RtChangedDateTime";
    private const string SystemAttributeCkTypeId = "CkTypeId";

    public List<CkTypeQueryColumn> GetColumns(CkId<CkTypeId> ckTypeId, CkTypeQueryColumnOptions? options = null)
    {
        return GetColumns(ckTypeId, options ?? CkTypeQueryColumnOptions.Default,
            [], 0);
    }

    public List<CkTypeQueryColumn> GetColumnsByRtCkId(RtCkId<CkTypeId> rtCkTypeId, CkTypeQueryColumnOptions? options = null)
    {
        return GetColumnsByRtCkId(rtCkTypeId, options ?? CkTypeQueryColumnOptions.Default,
            [], 0);
    }

    private List<CkTypeQueryColumn> GetColumnsByRtCkId(RtCkId<CkTypeId> rtCkTypeId, CkTypeQueryColumnOptions options,
        HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>> ignoredNavigations, int currentDepth)
    {

        if (!ckModelGraph.TypesByRtCk.TryGetValue(rtCkTypeId, out var ckTypeGraph))
        {
            throw DependencyGraphException.RtCkTypeIdNotFound(rtCkTypeId);
        }

        return GetColumns(ckTypeGraph, options, ignoredNavigations, currentDepth);
    }

    private List<CkTypeQueryColumn> GetColumns(CkId<CkTypeId> ckTypeId, CkTypeQueryColumnOptions options,
        HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>> ignoredNavigations, int currentDepth)
    {

        if (!ckModelGraph.Types.TryGetValue(ckTypeId, out var ckTypeGraph))
        {
            throw DependencyGraphException.CkTypeIdNotFound(ckTypeId);
        }

        return GetColumns(ckTypeGraph, options, ignoredNavigations, currentDepth);
    }

    private List<CkTypeQueryColumn> GetColumns(CkTypeGraph ckTypeGraph, CkTypeQueryColumnOptions options,
        HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>> ignoredNavigations, int currentDepth)
    {
        var columns = new List<CkTypeQueryColumn>();

        CollectTypeColumns(ckTypeGraph, columns);
        if (!options.IgnoreNavigationProperties)
        {
            CollectNavigationColumns(ckTypeGraph, columns, ignoredNavigations, options, currentDepth);
        }

        columns.Add(new CkTypeQueryColumn(SystemAttributeRtId.ToCamelCase(),
            [new(SystemAttributeRtId, PathType.Attribute)],
            AttributeValueTypesDto.String) { IsDataStream = true });
        columns.Add(new CkTypeQueryColumn(SystemAttributeCkTypeId.ToCamelCase(),
            [new(SystemAttributeCkTypeId, PathType.Attribute)],
            AttributeValueTypesDto.String) { IsDataStream = true });
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtWellKnownName.ToCamelCase(),
            [new(SystemAttributeRtWellKnownName, PathType.Attribute)],
            AttributeValueTypesDto.String) { IsDataStream = true });
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtVersion.ToCamelCase(),
            [new(SystemAttributeRtVersion, PathType.Attribute)],
            AttributeValueTypesDto.Int64));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtCreationDateTime.ToCamelCase(),
            [new(SystemAttributeRtCreationDateTime, PathType.Attribute)],
            AttributeValueTypesDto.DateTime) { IsDataStream = true });
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtChangedDateTime.ToCamelCase(),
            [new(SystemAttributeRtChangedDateTime, PathType.Attribute)],
            AttributeValueTypesDto.DateTime) { IsDataStream = true });
        return columns;
    }

    private void CollectNavigationColumns(CkTypeGraph typeGraph,
        List<CkTypeQueryColumn> columns, HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>> ignoredNavigations,
        CkTypeQueryColumnOptions options, int currentDepth)
    {
        if (options.MaxDepth.HasValue && currentDepth >= options.MaxDepth.Value)
        {
            return;
        }

        foreach (var ckTypeAssociationGraphGrouping in typeGraph.Associations.Out.All.GroupBy(x =>
                     x.NavigationPropertyName))
        {
            var ckTypeAssociationDirectionTuples = new List<CkTypeAssociationTuple>();
            foreach (var typeAssociationGraph in ckTypeAssociationGraphGrouping)
            {
                var ignoreTuple = new Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>(
                    typeAssociationGraph.TargetCkTypeId, typeAssociationGraph.CkRoleId);
                if (!ignoredNavigations.Add(ignoreTuple))
                {
                    continue;
                }

                if (!ckModelGraph.Types.TryGetValue(typeAssociationGraph.TargetCkTypeId, out var ckType))
                {
                    throw DependencyGraphException.CkTypeIdNotFound(typeAssociationGraph.TargetCkTypeId);
                }

                ckTypeAssociationDirectionTuples.AddRange(ckType.GetAllDerivedTypes(true)
                    .Select(t =>
                        new CkTypeAssociationTuple(t, typeAssociationGraph.CkRoleId,
                            typeAssociationGraph.Multiplicity)));
            }

            if (!ckTypeAssociationDirectionTuples.Any())
            {
                continue; // All Ck types are abstract for that association
            }

            // For N:M associations, create totalCount and exists columns per navigation property grouping
            CollectNtoMColumns(ckTypeAssociationGraphGrouping.Key, ckTypeAssociationDirectionTuples, columns);

            foreach (var ckTypeAssociationDirectionTuple in ckTypeAssociationDirectionTuples)
            {
                if (ckTypeAssociationDirectionTuple.Multiplicity == MultiplicitiesDto.ZeroOrOne ||
                    ckTypeAssociationDirectionTuple.Multiplicity == MultiplicitiesDto.One)
                {
                    var subColumns = GetColumns(ckTypeAssociationDirectionTuple.CkTypeId, options with { IgnoreNavigationProperties = false },
                        new HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>>(ignoredNavigations), currentDepth + 1);
                    foreach (var subColumn in subColumns)
                    {
                        var queryColumn = new CkTypeQueryColumn(
                            ckTypeAssociationGraphGrouping.Key.ToCamelCase() + Separator +
                            ckTypeAssociationDirectionTuple.CkTypeId.ToRtCkId().GetTypeName() + NavigationSeparator +
                            subColumn.Path,
                            [
                                new(ckTypeAssociationGraphGrouping.Key, PathType.Navigation),
                                new(ckTypeAssociationDirectionTuple.CkTypeId.ToRtCkId().GetTypeName(), PathType.TargetCkTypeId),
                                ..subColumn.AccessPathList
                            ], subColumn.ValueType, subColumn.AssociationTuple, subColumn.Description);
                        columns.Add(queryColumn);
                    }
                }
            }
        }

        // Also collect N:M columns for inbound associations
        foreach (var ckTypeAssociationGraphGrouping in typeGraph.Associations.In.All.GroupBy(x =>
                     x.NavigationPropertyName))
        {
            var ckTypeAssociationDirectionTuples = new List<CkTypeAssociationTuple>();
            foreach (var typeAssociationGraph in ckTypeAssociationGraphGrouping)
            {
                if (!ckModelGraph.Types.TryGetValue(typeAssociationGraph.TargetCkTypeId, out var ckType))
                {
                    throw DependencyGraphException.CkTypeIdNotFound(typeAssociationGraph.TargetCkTypeId);
                }

                ckTypeAssociationDirectionTuples.AddRange(ckType.GetAllDerivedTypes(true)
                    .Select(t =>
                        new CkTypeAssociationTuple(t, typeAssociationGraph.CkRoleId,
                            typeAssociationGraph.Multiplicity)));
            }

            if (!ckTypeAssociationDirectionTuples.Any())
            {
                continue;
            }

            CollectNtoMColumns(ckTypeAssociationGraphGrouping.Key, ckTypeAssociationDirectionTuples, columns);
        }
    }

    private void CollectNtoMColumns(string navigationPropertyName,
        List<CkTypeAssociationTuple> tuples, List<CkTypeQueryColumn> columns)
    {
        var firstNtoM = tuples.FirstOrDefault(t => t.Multiplicity == MultiplicitiesDto.N);
        if (firstNtoM == null)
        {
            return;
        }

        var targetTypeName = firstNtoM.CkTypeId.ToRtCkId().GetTypeName();
        var navNameCamel = navigationPropertyName.ToCamelCase();
        var pathPrefix = navNameCamel + Separator + targetTypeName + AssociationMetaSeparator;

        // totalCount column
        columns.Add(new CkTypeQueryColumn(
            pathPrefix + "totalCount",
            [
                new(navigationPropertyName, PathType.Navigation),
                new(targetTypeName, PathType.TargetCkTypeId),
            ], AttributeValueTypesDto.Int64, firstNtoM));

        // exists column
        columns.Add(new CkTypeQueryColumn(
            pathPrefix + "exists",
            [
                new(navigationPropertyName, PathType.Navigation),
                new(targetTypeName, PathType.TargetCkTypeId),
            ], AttributeValueTypesDto.Boolean, firstNtoM));
    }

    private void CollectTypeColumns(CkTypeWithAttributesGraph current, List<CkTypeQueryColumn> columns)
    {
        foreach (var attribute in current.AllAttributes.Values)
        {
            if (!ckModelGraph.Attributes.TryGetValue(attribute.CkAttributeId, out var attributeGraph))
            {
                throw DependencyGraphException.AttributeNotFound(attribute.CkAttributeId);
            }

            var attributeNamePascalCase = attribute.AttributeName;
            var attributeNameCamelCase = attribute.AttributeName.ToCamelCase();
            var description = attribute.Description;

            switch (attributeGraph.ValueType)
            {
                case AttributeValueTypesDto.Record:
                    if (attributeGraph.ValueCkRecordId == null)
                    {
                        throw DependencyGraphException.CkRecordIdNotDefined(attributeGraph.CkAttributeId);
                    }

                    if (!ckModelGraph.Records.TryGetValue(attributeGraph.ValueCkRecordId, out var recordGraph))
                    {
                        throw DependencyGraphException.RecordNotFound(attributeGraph.ValueCkRecordId);
                    }

                    var recordColumns = new List<CkTypeQueryColumn>();
                    CollectTypeColumns(recordGraph, recordColumns);
                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(attributeNameCamelCase + Separator + c.Path, l, c.ValueType,
                            description: c.Description);
                    }));
                    break;
                case AttributeValueTypesDto.RecordArray:
                    if (attributeGraph.ValueCkRecordId == null)
                    {
                        throw DependencyGraphException.CkRecordIdNotDefined(attributeGraph.CkAttributeId);
                    }

                    if (!ckModelGraph.Records.TryGetValue(attributeGraph.ValueCkRecordId, out recordGraph))
                    {
                        throw DependencyGraphException.RecordNotFound(attributeGraph.ValueCkRecordId);
                    }

                    recordColumns = [];
                    CollectTypeColumns(recordGraph, recordColumns);

                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(FirstElement, PathType.ArrayIndex));
                        l.Insert(0, new(attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(
                            attributeNameCamelCase + string.Format(Array, FirstElement) + Separator + c.Path, l,
                            true, c.Description);
                    }));

                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(AllElements, PathType.ArrayIndex));
                        l.Insert(0, new(attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(
                            attributeNameCamelCase + string.Format(Array, AllElements) + Separator + c.Path, l,
                            true, c.Description);
                    }));

                    break;
                case AttributeValueTypesDto.StringArray:
                case AttributeValueTypesDto.IntArray:

                    var l = new List<PathTerm>
                        { new(attributeNamePascalCase, PathType.Attribute), new(FirstElement, PathType.ArrayIndex) };
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase + string.Format(Array, FirstElement), l,
                        attributeGraph.ValueType, description: description));

                    l = [new(attributeNamePascalCase, PathType.Attribute), new(AllElements, PathType.ArrayIndex)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase + string.Format(Array, AllElements), l,
                        attributeGraph.ValueType, description: description));
                    break;
                case AttributeValueTypesDto.Enum:
                    if (attributeGraph.ValueCkEnumId == null)
                    {
                        throw DependencyGraphException.CkEnumIdNotDefined(attributeGraph.CkAttributeId);
                    }

                    l = [new(attributeNamePascalCase, PathType.Attribute)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase, l, attributeGraph.ValueCkEnumId,
                        description) { IsDataStream = attribute.IsDataStream });
                    break;
                default:
                    l = [new(attributeNamePascalCase, PathType.Attribute)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase, l, attributeGraph.ValueType,
                        description: description) { IsDataStream = attribute.IsDataStream });
                    break;
            }
        }
    }
}
