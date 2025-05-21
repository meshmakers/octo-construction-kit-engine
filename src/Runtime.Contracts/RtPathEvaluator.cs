using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Implements a path evaluator for attribute paths
/// </summary>
public static class RtPathEvaluator
{
    [DebuggerDisplay("Term = {Term} (Locator Count = {Locators.Count})")]
    private record PathTuple(PathTerm? Term, List<PathLocator> Locators);

    [DebuggerDisplay(
        "Entity = {RtTypeWithAttributes} (Attribute = {CkTypeAttributeGraph}): Index = {Index}, Value = {Value}")]
    private record PathLocator
    {
        public PathLocator(RtTypeWithAttributes? RtTypeWithAttributes,
            CkTypeAttributeGraph? CkTypeAttributeGraph,
            int? Index,
            object? Value)
        {
            this.RtTypeWithAttributes = RtTypeWithAttributes;
            this.CkTypeAttributeGraph = CkTypeAttributeGraph;
            this.Index = Index;
            this.Value = Value;
        }

        public RtTypeWithAttributes? RtTypeWithAttributes { get; set; }
        public CkTypeAttributeGraph? CkTypeAttributeGraph { get; init; }
        public int? Index { get; init; }
        public object? Value { get; set; }
    }

    private const string PatternString =
        @"(?:(?<=^)|(?<=->)|(?<=\.))(?<navigationProperty>[^.\[\]\->]+)(?=\.[^.\[\]\->]+->)  
          | \.(?<targetCkTypeId>[^.\[\]\->]+)(?=->)                                          
          | \[(?<arrayIndex>-?\d+|\*)\]                                                    
          | (?:(?<=^)|(?<=\.|->))(?<property>[^.\[\]\->]+)(?!->)";

    private const string MatchPatternString =
        @"^(?:[^.\[\]\->]+(?:\[(?:-?\d+|\*)\])*)(?:(?:\.[^.\[\]\->]+(?:\[(?:-?\d+|\*)\])*)|(?:\.[^.\[\]\->]+(?:\[(?:-?\d+|\*)\])*)->[^.\[\]\->]+(?:\[(?:-?\d+|\*)\])*)*$";

    // This regex is used to parse a path expression in the form of "property1.property2.property3[0]"
    // or "property1->property2.property3" or "property1[0].property2.property3"
    // or "property1[*].property2.property3".
    // It captures the following groups:
    // - property: the name of the property
    // - arrayIndex: the index of the array element (if any)
    // - navigationProperty: the name of the navigation property (if any)
    // - targetCkTypeId: the target construction kit type id (if any)
    // The regex uses non-capturing groups (?:...) to group the different parts of the expression
    private static readonly Regex Regex = new(PatternString,
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    // This regex is used to validate the path expression
    // It checks that the path expression does not contain any invalid characters
    private static readonly Regex MatchRegex =
        new(MatchPatternString, RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    /// <summary>
    /// Gets the path as a string from a list of path terms
    /// </summary>
    /// <param name="pathTerms">List of path terms</param>
    /// <returns>The path as a string</returns>
    public static string GetPath(IEnumerable<PathTerm> pathTerms)
    {
        StringBuilder sb = new();

        PathType? lastPathType = null;
        foreach (var pathTerm in pathTerms)
        {
            if (lastPathType != null)
            {
                sb.Append(lastPathType != PathType.TargetCkTypeId ? "." : "->");
            }

            switch (pathTerm.Type)
            {
                case PathType.ArrayIndex:
                    sb.Append($"[{pathTerm.Value}]");
                    break;
                default:
                    sb.Append(pathTerm.Value.ToCamelCase());
                    break;
            }

            lastPathType = pathTerm.Type;
        }


        return sb.ToString();
    }


    /// <summary>
    /// Tokenizes a path into a list of path terms
    /// </summary>
    /// <param name="path">Path to be tokenized</param>
    /// <exception cref="InvalidPathException">Indicates that the term is invalid.</exception>
    /// <returns>A list of path terms with value and type</returns>
    // ReSharper disable once MemberCanBePrivate.Global
    public static List<PathTerm> TokenizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw InvalidPathException.NoEmptyPaths();
        }

        var validPath = MatchRegex.Match(path);
        if (!validPath.Success)
        {
            throw InvalidPathException.InvalidPathTerm(path);
        }

        var tokens = new List<PathTerm>();
        foreach (Match match in Regex.Matches(path))
        {
            // If the group name "property" contains a value, it is a property name.
            if (match.Groups["property"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["property"].Value.ToCamelCase(), PathType.Attribute));
            }
            else if (match.Groups["navigationProperty"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["navigationProperty"].Value.ToCamelCase(),
                    PathType.Navigation));
            }
            else if (match.Groups["targetCkTypeId"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["targetCkTypeId"].Value.ToCamelCase(),
                    PathType.TargetCkTypeId));
            }
            // Otherwise, we check if the arrayIndex group was successful.
            else if (match.Groups["arrayIndex"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["arrayIndex"].Value, PathType.ArrayIndex));
            }
        }

        return tokens;
    }


    /// <summary>
    /// Gets the value of an attribute path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>The value of the attribute path</returns>
    public static object? GetValue(ICkCacheService ckCacheService, string tenantId, RtTypeWithAttributes root,
        string path)
    {
        var tokens = TokenizePath(path);
        return GetValueByPath(ckCacheService, tenantId, root, tokens);
    }

    /// <summary>
    /// Gets the value of an attribute path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>The value of the attribute path</returns>
    public static object? GetValue(ICkCacheService ckCacheService, string tenantId, RtTypeWithAttributes root,
        IEnumerable<PathTerm> path)
    {
        return GetValueByPath(ckCacheService, tenantId, root, path.ToList());
    }

    /// <summary>
    /// Sets the value of an attribute path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <param name="value">Value to be set</param>
    public static void SetValue(ICkCacheService ckCacheService, string tenantId, RtTypeWithAttributes root,
        IEnumerable<PathTerm> path, object? value)
    {
        var pathList = path.ToList();
        SetValueByPath(ckCacheService, tenantId, root, pathList, value);
    }

    /// <summary>
    /// Sets the value of an attribute path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <param name="value">Value to be set</param>
    public static void SetValue(ICkCacheService ckCacheService, string tenantId, RtTypeWithAttributes root, string path,
        object? value)
    {
        var tokens = TokenizePath(path);
        SetValueByPath(ckCacheService, tenantId, root, tokens, value);
    }

    /// <summary>
    /// Tokenizes a list of paths into a list of traversal navigation pairs
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="ckTypeId">Construction kit type id the association belongs to</param>
    /// <param name="paths">List of paths to be evaluated</param>
    /// <returns>A list of navigation pairs, an empty list is returned if no navigation property has been used</returns>
    public static List<NavigationPair> TokenizeAndGetNavigationPairs(ICkCacheService ckCacheService, string tenantId,
        CkId<CkTypeId> ckTypeId,
        IEnumerable<string> paths)
    {
        List<NavigationPair> navigationPairs = new();
        foreach (var path in paths)
        {
            var navigationPair = TokenizeAndGetNavigationPair(ckCacheService, tenantId, ckTypeId, path);
            if (navigationPair != null)
            {
                var existingNavigationPair = navigationPairs.SingleOrDefault(x =>
                    x.CkRoleId == navigationPair.CkRoleId &&
                    x.Direction == navigationPair.Direction &&
                    x.TargetCkTypeId == navigationPair.TargetCkTypeId);
                if (existingNavigationPair != null)
                {
                    // We ensure that navigation pairs with the same role id and direction are merged
                    existingNavigationPair.Merge(navigationPair);
                }
                else
                {
                    navigationPairs.Add(navigationPair);
                }
            }
        }

        return navigationPairs;
    }

    /// <summary>
    /// Tokenizes a path into a traversable navigation pair
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="ckTypeId">Construction kit type id the association belongs to</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>If navigation is used, the corresponding navigation pair is returned</returns>
    public static NavigationPair? TokenizeAndGetNavigationPair(ICkCacheService ckCacheService, string tenantId,
        CkId<CkTypeId> ckTypeId, string path)
    {
        NavigationPair? navigationPair = null;
        NavigationPair? currentNavigationPair = null;
        var tokens = TokenizePath(path);

        // Get all combinations in tokens list with type= PathType.Navigation and PathType.TargetCkTypeId
        var combinations = new List<Tuple<PathTerm, PathTerm>>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type == PathType.Navigation)
            {
                var currentToken = tokens[i];
                var nextToken = tokens[i + 1];
                if (nextToken.Type != PathType.TargetCkTypeId)
                {
                    throw InvalidPathException.InvalidPathTermTargetCkTypeIdMissing(path, currentToken);
                }

                combinations.Add(Tuple.Create(tokens[i], nextToken));
            }
        }

        if (!ckCacheService.TryGetCkType(tenantId, ckTypeId, out var ckTypeGraph) ||
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            ckTypeGraph == null)
        {
            throw InvalidPathException.CkTypeIdNotFound(tenantId, ckTypeId);
        }

        foreach (var combination in combinations)
        {
            var navigationProperty = combination.Item1;
            var targetTypeProperty = combination.Item2;

            var inAssociations = ckTypeGraph.Associations.In.All
                .Where(a => a.NavigationPropertyName == navigationProperty.Value.ToPascalCase() &&
                            ckCacheService.GetCkType(tenantId, a.TargetCkTypeId).GetAllDerivedTypes(true)
                                .Select(t => t.GetTypeName()).Contains(targetTypeProperty.Value)).ToList();
            var outAssociations = ckTypeGraph.Associations.Out.All
                .Where(a => a.NavigationPropertyName == navigationProperty.Value.ToPascalCase() &&
                            ckCacheService.GetCkType(tenantId, a.TargetCkTypeId).GetAllDerivedTypes(true)
                                .Select(t => t.GetTypeName()).Contains(targetTypeProperty.Value)).ToList();

            if (inAssociations.Count == 0 && outAssociations.Count == 0)
            {
                throw InvalidPathException.AssociationNotFound(path, navigationProperty, targetTypeProperty);
            }

            foreach (var association in inAssociations)
            {
                var realTargetCkTypeId = ckCacheService.GetCkType(tenantId, association.TargetCkTypeId)
                    .GetAllDerivedTypes(true)
                    .First(t => t.GetTypeName() == targetTypeProperty.Value);
                ckTypeGraph = ckCacheService.GetCkType(tenantId, realTargetCkTypeId);

                var pathTerms = tokens.TakeWhile(t => t != targetTypeProperty).ToList();
                pathTerms.Add(targetTypeProperty);
                var roleIdDirectionPair = new NavigationPair(pathTerms,
                    [tokens.SkipWhile(t => t != targetTypeProperty).Skip(1)], association.CkRoleId,
                    GraphDirections.Inbound,
                    realTargetCkTypeId);

                if (currentNavigationPair != null)
                {
                    currentNavigationPair.InnerNavigationPairs.Add(roleIdDirectionPair);
                }

                currentNavigationPair = roleIdDirectionPair;
            }

            foreach (var association in outAssociations)
            {
                var realTargetCkTypeId = ckCacheService.GetCkType(tenantId, association.TargetCkTypeId)
                    .GetAllDerivedTypes(true)
                    .First(t => t.GetTypeName() == targetTypeProperty.Value);
                ckTypeGraph = ckCacheService.GetCkType(tenantId, realTargetCkTypeId);

                var pathTerms = tokens.TakeWhile(t => t != targetTypeProperty).ToList();
                pathTerms.Add(targetTypeProperty);
                var roleIdDirectionPair = new NavigationPair(pathTerms,
                    [tokens.SkipWhile(t => t != targetTypeProperty).Skip(1)], association.CkRoleId,
                    GraphDirections.Outbound,
                    realTargetCkTypeId);

                if (currentNavigationPair != null)
                {
                    currentNavigationPair.InnerNavigationPairs.Add(roleIdDirectionPair);
                }

                currentNavigationPair = roleIdDirectionPair;
            }

            navigationPair ??= currentNavigationPair;
        }

        return navigationPair;
    }

    private static object? GetValueByPath(ICkCacheService ckCacheService, string tenantId,
        RtTypeWithAttributes rtTypeWithAttributes, List<PathTerm> tokens)
    {
        var evaluatedPath = MapPath(ckCacheService, tenantId, rtTypeWithAttributes, tokens);

        var pathTuple = evaluatedPath.Last();

        List<object?> results = new List<object?>();
        foreach (var pathTupleLocator in pathTuple.Locators)
        {
            results.Add(pathTupleLocator.Value);
        }

        return results.Count switch
        {
            0 => null,
            1 => results[0],
            _ => results
        };
    }

    private static void SetValueByPath(ICkCacheService ckCacheService, string tenantId,
        RtTypeWithAttributes rtTypeWithAttributes,
        List<PathTerm> tokens, object? setValue)
    {
        var evaluatedPath = MapPath(ckCacheService, tenantId, rtTypeWithAttributes, tokens);

        if (setValue != null)
        {
            RtRecord? lastRecord = null;
            foreach (var tuple in evaluatedPath)
            {
                if (tuple.Term?.Type == PathType.Attribute)
                {
                    foreach (var tupleLocator in tuple.Locators)
                    {
                        tupleLocator.RtTypeWithAttributes ??= lastRecord;

                        if (tupleLocator.Value == null && tupleLocator.CkTypeAttributeGraph != null
                                                       && tupleLocator.CkTypeAttributeGraph.ValueCkRecordId != null
                                                       && tupleLocator.CkTypeAttributeGraph?.ValueType ==
                                                       AttributeValueTypesDto.Record)
                        {
                            if (tupleLocator.RtTypeWithAttributes == null)
                            {
                                throw InvalidPathException.PathNotSettableBecauseNull(tokens);
                            }

                            lastRecord = new RtRecord
                            {
                                CkRecordId = tupleLocator.CkTypeAttributeGraph.ValueCkRecordId,
                            };
                            tupleLocator.RtTypeWithAttributes.SetAttributeValue(
                                tupleLocator.CkTypeAttributeGraph.AttributeName, AttributeValueTypesDto.Record,
                                lastRecord);
                            tupleLocator.Value = lastRecord;
                        }
                    }
                }
            }
        }

        var pathTuple = evaluatedPath.Last();

        if (pathTuple.Locators.Count == 0)
        {
            throw InvalidPathException.PathNotSettable(pathTuple.Term, tokens);
        }

        foreach (var pathTupleLocator in pathTuple.Locators)
        {
            if (pathTupleLocator.RtTypeWithAttributes == null)
            {
                // If the value is null and the object is null, we do nothing (a record does not exist but also
                // is not needed to be created)
                if (setValue == null)
                {
                    return;
                }

                throw InvalidPathException.PathNotSettableBecauseNull(tokens);
            }

            if (pathTuple.Term == null)
            {
                throw InvalidPathException.PathNotSettable(pathTupleLocator.RtTypeWithAttributes, tokens);
            }

            if (pathTuple.Term.Value == nameof(RtEntity.RtId) ||
                pathTuple.Term.Value == nameof(RtEntity.RtWellKnownName) ||
                pathTuple.Term.Value == nameof(RtEntity.RtVersion) ||
                pathTuple.Term.Value == nameof(RtEntity.RtCreationDateTime) ||
                pathTuple.Term.Value == nameof(RtEntity.RtChangedDateTime))
            {
                pathTupleLocator.RtTypeWithAttributes.GetType().GetProperty(pathTuple.Term.Value)
                    ?.SetValue(pathTupleLocator.RtTypeWithAttributes, setValue);
            }

            if (pathTupleLocator.CkTypeAttributeGraph == null)
            {
                throw InvalidPathException.PathNotSettable(pathTupleLocator.RtTypeWithAttributes, pathTuple.Term);
            }

            switch (pathTupleLocator.CkTypeAttributeGraph.ValueType)
            {
                case AttributeValueTypesDto.IntArray:
                case AttributeValueTypesDto.StringArray:
                case AttributeValueTypesDto.RecordArray:

                    if (pathTupleLocator.Index == null)
                    {
                        throw InvalidPathException.PathNotSettable(pathTupleLocator.RtTypeWithAttributes,
                            pathTuple.Term);
                    }

                    if (!pathTupleLocator.RtTypeWithAttributes.Attributes.TryGetValue(pathTupleLocator
                            .CkTypeAttributeGraph.AttributeName, out var value))
                    {
                        throw InvalidPathException.CannotGetAttributeValue(
                            pathTupleLocator.RtTypeWithAttributes, pathTuple.Term);
                    }

                    if (!(value is IEnumerable values))
                    {
                        throw InvalidPathException.AttributeValueIsNotArray(
                            pathTupleLocator.RtTypeWithAttributes, pathTuple.Term);
                    }

                    var x = values.Cast<object?>().ToList();
                    x[pathTupleLocator.Index.Value] = setValue;

                    pathTupleLocator.RtTypeWithAttributes.SetAttributeValue(
                        pathTupleLocator.CkTypeAttributeGraph.AttributeName,
                        pathTupleLocator.CkTypeAttributeGraph.ValueType, x);
                    break;
                default:
                    pathTupleLocator.RtTypeWithAttributes.SetAttributeValue(
                        pathTupleLocator.CkTypeAttributeGraph.AttributeName,
                        pathTupleLocator.CkTypeAttributeGraph.ValueType, setValue);
                    break;
            }
        }
    }

    private static List<PathTuple> MapPath(ICkCacheService ckCacheService, string tenantId,
        RtTypeWithAttributes rtTypeWithAttributes,
        List<PathTerm> tokens)
    {
        // This list contains the current state of path evaluation (transformation from path to object structure)
        var evaluatedPath = new List<PathTuple>([
            new PathTuple(null, [new PathLocator(rtTypeWithAttributes, null, null, rtTypeWithAttributes)])
        ]);

        // We evaluate the path terms to find the target attribute
        foreach (var token in tokens)
        {
            var lastPathTuple = evaluatedPath.Last();
            var newPathLocators = new List<PathLocator>();
            evaluatedPath.Add(new PathTuple(token, newPathLocators));

            foreach (var locator in lastPathTuple.Locators)
            {
                // Navigation property
                if (token.Type == PathType.Navigation)
                {
                    if (locator.RtTypeWithAttributes is RtEntityGraphItem rtEntityGraphItem)
                    {
                        var navigationEnds = rtEntityGraphItem.Associations.Where(x =>
                                x.NavigationPropertyName == token.Value.ToPascalCase())
                            .ToList();

                        if (navigationEnds.Count == 0)
                        {
                            if (rtEntityGraphItem.CkTypeId != null)
                            {
                                var ckTypeGraph = ckCacheService.GetCkType(tenantId, rtEntityGraphItem.CkTypeId);
                                if (ckTypeGraph == null)
                                {
                                    throw InvalidPathException.CkTypeIdNotFound(tenantId, rtEntityGraphItem.CkTypeId);
                                }

                                var ckTypeAssociationGraphs = ckTypeGraph.Associations.Out.All
                                    .Where(x => x.NavigationPropertyName == token.Value.ToPascalCase())
                                    .ToList();
                                ckTypeAssociationGraphs.AddRange(ckTypeGraph.Associations.In.All
                                    .Where(x => x.NavigationPropertyName == token.Value.ToPascalCase()));

                                if (ckTypeAssociationGraphs.Count == 0)
                                {
                                    throw InvalidPathException.NavigationPropertyNotFound(tokens, token);
                                }

                                foreach (var ckTypeAssociationGraph in ckTypeAssociationGraphs)
                                {
                                    navigationEnds.Add(new NavigationEnd
                                    {
                                        AssociationRoleId = ckTypeAssociationGraph.CkRoleId,
                                        AssociationId = OctoObjectId.Empty,
                                        NavigationPropertyName = ckTypeAssociationGraph.NavigationPropertyName,
                                        TargetCkTypeId = ckTypeAssociationGraph.TargetCkTypeId,
                                        Targets = new List<RtEntityGraphItem>()
                                    });
                                }

                                rtEntityGraphItem.Associations.AddRange(navigationEnds);
                            }
                            else
                            {
                                throw InvalidPathException.NavigationPropertyNotFound(tokens, token);
                            }
                        }

                        newPathLocators.Add(new PathLocator(locator.RtTypeWithAttributes, null,
                            null, navigationEnds));
                    }
                    else
                    {
                        throw InvalidPathException.InvalidNavigationPropertyToken(locator.RtTypeWithAttributes, token);
                    }
                }
                else if (token.Type == PathType.TargetCkTypeId)
                {
                    if (locator.Value is not List<NavigationEnd> navigationEnds)
                    {
                        throw InvalidPathException.InvalidNavigationPropertyToken(locator.RtTypeWithAttributes, token);
                    }

                    var filteredNavigationEnds = navigationEnds
                        .Where(ne =>
                            ckCacheService.GetCkType(tenantId, ne.TargetCkTypeId).GetAllDerivedTypes(true)
                                .Select(t => t.GetTypeName()).Contains(token.Value)).ToList();

                    if (filteredNavigationEnds.Count == 0)
                    {
                        throw InvalidPathException.TargetCkTypeIdNotFound(tokens, token);
                    }

                    if (filteredNavigationEnds.Count == 1)
                    {
                        var navigationEnd = navigationEnds.First();
                        navigationEnd.TargetCkTypeId = ckCacheService.GetCkType(tenantId, navigationEnd.TargetCkTypeId)
                            .GetAllDerivedTypes(true).Single(t => t.GetTypeName() == token.Value);
                        if (navigationEnd.Targets.Count() == 1)
                        {
                            newPathLocators.Add(new PathLocator(navigationEnd.Targets.First(), null,
                                null, navigationEnd.Targets.First()));
                        }
                        else
                        {
                            var targets = navigationEnd.Targets.ToList();
                            for (int i = 0; i < targets.Count; i++)
                            {
                                var target = targets[i];
                                newPathLocators.Add(new PathLocator(target, null,
                                    i, target));
                            }
                        }
                    }
                    else
                    {
                        throw InvalidPathException.MultipleNavigationEndsUnsupported(locator.RtTypeWithAttributes,
                            token);
                    }
                }
                // Attribute
                else if (token.Type == PathType.Attribute)
                {
                    if (locator.Value == null)
                    {
                        if (locator.CkTypeAttributeGraph?.ValueCkRecordId != null &&
                            ckCacheService.TryGetCkRecord(tenantId, locator.CkTypeAttributeGraph.ValueCkRecordId,
                                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                out var ckRecordGraph) && ckRecordGraph != null)
                        {
                            if (ckRecordGraph.AllAttributesByName.TryGetValue(token.Value.ToPascalCase(),
                                    out var ckRecordAttributeGraph))
                            {
                                newPathLocators.Add(new PathLocator(null,
                                    ckRecordAttributeGraph, null, null));
                                continue;
                            }
                        }

                        newPathLocators.Add(new(null, null, null, null));
                        continue;
                    }

                    if (!(locator.Value is RtTypeWithAttributes valueRtTypeWithAttribute))
                    {
                        throw InvalidPathException.CannotGetAttributeValue(locator.RtTypeWithAttributes, token);
                    }

                    if (token.Value.ToPascalCase() == nameof(RtEntity.RtId) ||
                        token.Value.ToPascalCase() == nameof(RtEntity.RtWellKnownName) ||
                        token.Value.ToPascalCase() == nameof(RtEntity.RtVersion) ||
                        token.Value.ToPascalCase() == nameof(RtEntity.RtCreationDateTime) ||
                        token.Value.ToPascalCase() == nameof(RtEntity.RtChangedDateTime))
                    {
                        var value = valueRtTypeWithAttribute.GetType().GetProperty(token.Value.ToPascalCase())
                            ?.GetValue(valueRtTypeWithAttribute);
                        newPathLocators.Add(new PathLocator(valueRtTypeWithAttribute, null, null, value));
                        continue;
                    }

                    switch (valueRtTypeWithAttribute)
                    {
                        case RtEntity rtEntity
                            when ckCacheService.TryGetCkType(tenantId, rtEntity.GetCkTypeId(), out var ckTypeGraph)
                                 // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                 && ckTypeGraph != null &&
                                 ckTypeGraph.AllAttributesByName.TryGetValue(token.Value.ToPascalCase(),
                                     out var ckTypeAttributeGraph):
                            valueRtTypeWithAttribute.Attributes.TryGetValue(token.Value.ToPascalCase(),
                                out var entityValue);
                            newPathLocators.Add(new PathLocator(rtEntity, ckTypeAttributeGraph, null, entityValue));
                            continue;
                        case RtRecord rtRecord
                            when ckCacheService.TryGetCkRecord(tenantId, rtRecord.CkRecordId, out var ckRecordGraph)
                                 // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                 && ckRecordGraph != null &&
                                 ckRecordGraph.AllAttributesByName.TryGetValue(token.Value.ToPascalCase(),
                                     out var ckRecordAttributeGraph):
                            valueRtTypeWithAttribute.Attributes.TryGetValue(token.Value.ToPascalCase(),
                                out var recordValue);
                            newPathLocators.Add(
                                new PathLocator(rtRecord, ckRecordAttributeGraph, null, recordValue));

                            continue;
                        default:
                            throw InvalidPathException.CannotGetAttributeValue(locator.RtTypeWithAttributes, token);
                    }
                }
                else if (token.Type == PathType.ArrayIndex)
                {
                    if (locator.CkTypeAttributeGraph == null)
                    {
                        throw InvalidPathException.InvalidArrayIndexToken(locator.RtTypeWithAttributes, token);
                    }

                    if (locator.Value == null)
                    {
                        newPathLocators.Add(new PathLocator(locator.RtTypeWithAttributes, null, null, null));
                    }

                    var indexStr = token.Value;


                    if (locator.Value is IEnumerable<RtRecord> rtRecords)
                    {
                        var recordList = rtRecords.ToList();

                        if (indexStr == "*")
                        {
                            for (int i = 0; i < recordList.Count; i++)
                            {
                                var item = recordList.ElementAt(i);
                                newPathLocators.Add(new PathLocator(item, null, i, item));
                            }

                            continue;
                        }

                        if (!int.TryParse(indexStr, out int index))
                        {
                            throw InvalidPathException.InvalidArrayIndex(locator.RtTypeWithAttributes, token);
                        }

                        if (index >= 0)
                        {
                            if (index < recordList.Count)
                            {
                                var rtRecord = recordList.ElementAt(index);
                                newPathLocators.Add(locator with { Index = index, Value = rtRecord });
                            }
                            else
                            {
                                throw InvalidPathException.InvalidArrayIndex(locator.RtTypeWithAttributes, token);
                            }

                            continue;
                        }

                        var calcIndex = recordList.Count + index;

                        if (calcIndex < recordList.Count)
                        {
                            var rtRecord = recordList.ElementAt(calcIndex);
                            newPathLocators.Add(locator with { Index = calcIndex, Value = rtRecord });
                        }
                        else
                        {
                            throw InvalidPathException.InvalidArrayIndex(locator.RtTypeWithAttributes, token);
                        }

                        continue;
                    }

                    if (locator.Value is IEnumerable list)
                    {
                        var scalarValues = list.Cast<object>().ToList();
                        if (indexStr == "*")
                        {
                            for (int i = 0; i < scalarValues.Count; i++)
                            {
                                var item = scalarValues.ElementAt(i);
                                newPathLocators.Add(locator with { Index = i, Value = item });
                            }

                            continue;
                        }

                        if (!int.TryParse(indexStr, out int index))
                        {
                            throw InvalidPathException.InvalidArrayIndex(locator.RtTypeWithAttributes, token);
                        }

                        if (index >= 0)
                        {
                            if (index < scalarValues.Count)
                            {
                                var v = scalarValues.ElementAt(index);
                                newPathLocators.Add(locator with { Index = index, Value = v });
                            }
                            else
                            {
                                throw InvalidPathException.InvalidArrayIndex(locator.RtTypeWithAttributes, token);
                            }

                            continue;
                        }

                        var calcIndex = scalarValues.Count + index;

                        if (calcIndex >= 0 && calcIndex < scalarValues.Count)
                        {
                            var v = scalarValues.ElementAt(index);
                            newPathLocators.Add(locator with { Index = calcIndex, Value = v });
                        }
                        else
                        {
                            throw InvalidPathException.InvalidArrayIndex(locator.RtTypeWithAttributes, token);
                        }

                        continue;
                    }

                    throw InvalidPathException.InvalidArrayIndexData(locator.RtTypeWithAttributes, token);
                }
                else
                {
                    throw InvalidPathException.InvalidTokenType(token);
                }
            }
        }

        return evaluatedPath;
    }
}