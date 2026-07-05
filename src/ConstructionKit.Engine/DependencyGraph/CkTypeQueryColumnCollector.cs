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

    private CkTypeQueryColumnOptions _options = CkTypeQueryColumnOptions.Default;
    private CkId<CkTypeId>? _rootCkTypeId;
    private int _totalColumns;

    public List<CkTypeQueryColumn> GetColumns(CkId<CkTypeId> ckTypeId, CkTypeQueryColumnOptions? options = null)
    {
        _options = options ?? CkTypeQueryColumnOptions.Default;
        _rootCkTypeId = ckTypeId;
        _totalColumns = 0;
        return GetColumns(ckTypeId, _options, [], 0);
    }

    public List<CkTypeQueryColumn> GetColumnsByRtCkId(RtCkId<CkTypeId> rtCkTypeId, CkTypeQueryColumnOptions? options = null)
    {
        _options = options ?? CkTypeQueryColumnOptions.Default;
        _rootCkTypeId = null;
        _totalColumns = 0;
        return GetColumnsByRtCkId(rtCkTypeId, _options, [], 0);
    }

    /// <summary>
    /// Budget guard against combinatorial column explosion. Densely connected, cyclic association
    /// graphs (e.g. a 0..1 self-association on a root type all other types derive from, combined
    /// with the derived-type fan-out per navigation) make the per-path cycle guard insufficient —
    /// sibling branches re-explore the same subgraph, so the total path count grows exponentially.
    /// Counting every produced column and failing fast turns a multi-gigabyte runaway allocation
    /// into an immediate, actionable exception.
    /// </summary>
    private void TrackProducedColumns(int added)
    {
        _totalColumns += added;
        if (_options.MaxColumns is { } maxColumns && _totalColumns > maxColumns)
        {
            throw DependencyGraphException.QueryColumnLimitExceeded(_rootCkTypeId, maxColumns);
        }
    }

    private List<CkTypeQueryColumn> GetColumnsByRtCkId(RtCkId<CkTypeId> rtCkTypeId, CkTypeQueryColumnOptions options,
        HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>> ignoredNavigations, int currentDepth)
    {

        if (!ckModelGraph.TypesByRtCk.TryGetValue(rtCkTypeId, out var ckTypeGraph))
        {
            throw DependencyGraphException.RtCkTypeIdNotFound(rtCkTypeId);
        }

        _rootCkTypeId ??= ckTypeGraph.CkTypeId;
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
            AttributeValueTypesDto.String));
        columns.Add(new CkTypeQueryColumn(SystemAttributeCkTypeId.ToCamelCase(),
            [new(SystemAttributeCkTypeId, PathType.Attribute)],
            AttributeValueTypesDto.String));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtWellKnownName.ToCamelCase(),
            [new(SystemAttributeRtWellKnownName, PathType.Attribute)],
            AttributeValueTypesDto.String));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtVersion.ToCamelCase(),
            [new(SystemAttributeRtVersion, PathType.Attribute)],
            AttributeValueTypesDto.Int64));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtCreationDateTime.ToCamelCase(),
            [new(SystemAttributeRtCreationDateTime, PathType.Attribute)],
            AttributeValueTypesDto.DateTime));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtChangedDateTime.ToCamelCase(),
            [new(SystemAttributeRtChangedDateTime, PathType.Attribute)],
            AttributeValueTypesDto.DateTime));
        TrackProducedColumns(6);
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
                    CollectValueNavigationColumns(ckTypeAssociationGraphGrouping.Key,
                        ckTypeAssociationDirectionTuple, null, columns, ignoredNavigations, options, currentDepth);
                }
                else if (options.IncludeManyNavigations &&
                         ckTypeAssociationDirectionTuple.Multiplicity == MultiplicitiesDto.N)
                {
                    // N navigations resolve per row to the first matching target (deterministic
                    // order); the association tuple marks the column so consumers can tell it
                    // apart from plain 0..1 navigation (read-only, first-match semantics).
                    CollectValueNavigationColumns(ckTypeAssociationGraphGrouping.Key,
                        ckTypeAssociationDirectionTuple, ckTypeAssociationDirectionTuple, columns,
                        ignoredNavigations, options, currentDepth);
                }
            }
        }

        // Also collect columns for inbound associations. The reachable end of an inbound
        // navigation is the association's ORIGIN type (the type that declares the association),
        // not the graph's TargetCkTypeId — that one points at the type the graph is attached to.
        foreach (var ckTypeAssociationGraphGrouping in typeGraph.Associations.In.All.GroupBy(x =>
                     x.NavigationPropertyName))
        {
            var ckTypeAssociationDirectionTuples = new List<CkTypeAssociationTuple>();
            foreach (var typeAssociationGraph in ckTypeAssociationGraphGrouping)
            {
                if (!ckModelGraph.Types.TryGetValue(typeAssociationGraph.OriginCkTypeId, out var ckType))
                {
                    throw DependencyGraphException.CkTypeIdNotFound(typeAssociationGraph.OriginCkTypeId);
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

            if (!options.IncludeManyNavigations)
            {
                continue;
            }

            foreach (var ckTypeAssociationDirectionTuple in ckTypeAssociationDirectionTuples)
            {
                var ignoreTuple = new Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>(
                    ckTypeAssociationDirectionTuple.CkTypeId, ckTypeAssociationDirectionTuple.CkAssociationRoleId);
                if (!ignoredNavigations.Add(ignoreTuple))
                {
                    continue;
                }

                CollectValueNavigationColumns(ckTypeAssociationGraphGrouping.Key,
                    ckTypeAssociationDirectionTuple, ckTypeAssociationDirectionTuple, columns,
                    ignoredNavigations, options, currentDepth);
            }
        }
    }

    /// <summary>
    /// Produces the attribute value columns (<c>nav.type-&gt;attribute</c>) reached through one
    /// navigation tuple. <paramref name="associationTuple"/> is stamped on every produced column
    /// when the navigation crosses an N-multiplicity association (first-match semantics);
    /// pass <c>null</c> for plain 0..1/1 navigation to keep the historical column shape.
    /// </summary>
    private void CollectValueNavigationColumns(string navigationPropertyName,
        CkTypeAssociationTuple navigationTuple, CkTypeAssociationTuple? associationTuple,
        List<CkTypeQueryColumn> columns,
        HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>> ignoredNavigations,
        CkTypeQueryColumnOptions options, int currentDepth)
    {
        var subColumns = GetColumns(navigationTuple.CkTypeId, options with { IgnoreNavigationProperties = false },
            new HashSet<Tuple<CkId<CkTypeId>, CkId<CkAssociationRoleId>>>(ignoredNavigations), currentDepth + 1);
        foreach (var subColumn in subColumns)
        {
            var path = navigationPropertyName.ToCamelCase() + Separator +
                       navigationTuple.CkTypeId.ToRtCkId().GetTypeName() + NavigationSeparator +
                       subColumn.Path;
            var accessPathList = new List<PathTerm>
            {
                new(navigationPropertyName, PathType.Navigation),
                new(navigationTuple.CkTypeId.ToRtCkId().GetTypeName(), PathType.TargetCkTypeId)
            };
            accessPathList.AddRange(subColumn.AccessPathList);

            // Preserve the enum id through navigation descent (mirrors the record-descent rule):
            // the generic constructor would drop CkEnumId and break enum-name resolution for
            // navigated enum columns.
            var queryColumn = subColumn.CkEnumId != null
                ? new CkTypeQueryColumn(path, accessPathList, subColumn.CkEnumId, subColumn.Description)
                : new CkTypeQueryColumn(path, accessPathList, subColumn.ValueType,
                    associationTuple ?? subColumn.AssociationTuple, subColumn.Description);
            columns.Add(queryColumn);
        }

        TrackProducedColumns(subColumns.Count);
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

        TrackProducedColumns(2);
    }

    private void CollectTypeColumns(CkTypeWithAttributesGraph current, List<CkTypeQueryColumn> columns,
        HashSet<CkId<CkRecordId>>? recordPath = null)
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

                    recordPath ??= [];
                    if (!recordPath.Add(attributeGraph.ValueCkRecordId))
                    {
                        throw DependencyGraphException.RecordCycleDetected(attributeGraph.ValueCkRecordId);
                    }

                    var recordColumns = new List<CkTypeQueryColumn>();
                    CollectTypeColumns(recordGraph, recordColumns, recordPath);
                    recordPath.Remove(attributeGraph.ValueCkRecordId);
                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(attributeNamePascalCase, PathType.Attribute));
                        var nestedPath = attributeNameCamelCase + Separator + c.Path;
                        // Preserve the enum id through record descent so nested enum columns
                        // (e.g. amount.unit) still report which CK enum to resolve against —
                        // the generic constructor would drop CkEnumId and leave the column an
                        // enum with no enum id, breaking enum-name resolution for nested paths.
                        return c.CkEnumId != null
                            ? new CkTypeQueryColumn(nestedPath, l, c.CkEnumId, c.Description)
                            : new CkTypeQueryColumn(nestedPath, l, c.ValueType, description: c.Description);
                    }));
                    TrackProducedColumns(recordColumns.Count);
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

                    recordPath ??= [];
                    if (!recordPath.Add(attributeGraph.ValueCkRecordId))
                    {
                        throw DependencyGraphException.RecordCycleDetected(attributeGraph.ValueCkRecordId);
                    }

                    recordColumns = [];
                    CollectTypeColumns(recordGraph, recordColumns, recordPath);
                    recordPath.Remove(attributeGraph.ValueCkRecordId);

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

                    TrackProducedColumns(recordColumns.Count * 2);
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
                    TrackProducedColumns(2);
                    break;
                case AttributeValueTypesDto.Enum:
                    if (attributeGraph.ValueCkEnumId == null)
                    {
                        throw DependencyGraphException.CkEnumIdNotDefined(attributeGraph.CkAttributeId);
                    }

                    l = [new(attributeNamePascalCase, PathType.Attribute)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase, l, attributeGraph.ValueCkEnumId,
                        description));
                    TrackProducedColumns(1);
                    break;
                default:
                    l = [new(attributeNamePascalCase, PathType.Attribute)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase, l, attributeGraph.ValueType,
                        description: description));
                    TrackProducedColumns(1);
                    break;
            }
        }
    }
}
