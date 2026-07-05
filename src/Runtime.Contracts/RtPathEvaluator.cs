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
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Implements a path evaluator for attribute paths
/// </summary>
public static class RtPathEvaluator
{
    // ReSharper disable once NotResolvedInText
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
        @"(?:(?<=^)|(?<=->)|(?<=\.))(?<navigationProperty>[^.\[\]\->:]+)(?=\.[^.\[\]\->:]+(?:\[[^\[\]]+\])*(?:->|::))
          | \.(?<targetCkTypeId>[^.\[\]\->:]+)(?=(?:\[[^\[\]]+\])*(?:->|::))
          | ::(?<associationMeta>[^.\[\]\->:]+)
          | \[(?<arrayIndex>-?\d+|\*)\]
          | \[(?<entitySelector>[^\[\]=]+=[^\[\]]*)\]
          | (?:(?<=^)|(?<=\.|->))(?<property>[^.\[\]\->:]+)(?!->|::)";

    private const string MatchPatternString =
        @"^(?:[^.\[\]\->:]+(?:\[(?:-?\d+|\*|[^\[\]=]+=[^\[\]]*)\])*)(?:(?:\.[^.\[\]\->:]+(?:\[(?:-?\d+|\*|[^\[\]=]+=[^\[\]]*)\])*)|(?:\.[^.\[\]\->:]+(?:\[(?:-?\d+|\*|[^\[\]=]+=[^\[\]]*)\])*)->[^.\[\]\->:]+(?:\[(?:-?\d+|\*|[^\[\]=]+=[^\[\]]*)\])*|(?:\.[^.\[\]\->:]+(?:\[(?:-?\d+|\*|[^\[\]=]+=[^\[\]]*)\])*)::(?:[^.\[\]\->:]+))*$";

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
            // Bracket terms attach directly to the preceding segment (attr[0], type[key=value])
            // and must neither emit a separator nor shift the separator context for the term
            // that follows them (type[key=value]->attr still separates with ->).
            var isBracketTerm = pathTerm.Type is PathType.ArrayIndex or PathType.EntitySelector;
            if (lastPathType != null && !isBracketTerm)
            {
                if (pathTerm.Type == PathType.AssociationMeta)
                {
                    sb.Append("::");
                }
                else
                {
                    sb.Append(lastPathType != PathType.TargetCkTypeId ? "." : "->");
                }
            }

            switch (pathTerm.Type)
            {
                case PathType.ArrayIndex:
                case PathType.EntitySelector:
                    sb.Append($"[{pathTerm.Value}]");
                    break;
                default:
                    sb.Append(pathTerm.Value.ToCamelCase());
                    break;
            }

            if (!isBracketTerm)
            {
                lastPathType = pathTerm.Type;
            }
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
            else if (match.Groups["associationMeta"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["associationMeta"].Value,
                    PathType.AssociationMeta));
            }
            // Otherwise, we check if the arrayIndex group was successful.
            else if (match.Groups["arrayIndex"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["arrayIndex"].Value, PathType.ArrayIndex));
            }
            else if (match.Groups["entitySelector"].Success)
            {
                // Raw key=value payload; parsed by ParseEntitySelector when the navigation pair
                // is built. Do not camel-case — the value part is a literal.
                tokens.Add(new PathTerm(match.Groups["entitySelector"].Value, PathType.EntitySelector));
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
    /// <param name="attributeValueResolveFlags">Flags to control how attribute values are resolved</param>
    /// <returns>The value of the attribute path</returns>
    public static object? GetValue(ICkCacheService ckCacheService, string tenantId, RtTypeWithAttributes root,
        string path,
        AttributeValueResolveFlags attributeValueResolveFlags = AttributeValueResolveFlags.Default)
    {
        var tokens = TokenizePath(path);
        return GetValueByPath(ckCacheService, tenantId, root, tokens, attributeValueResolveFlags);
    }

    /// <summary>
    /// Gets the value of an attribute path
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <param name="attributeValueResolveFlags">Flags to control how attribute values are resolved</param>
    /// <returns>The value of the attribute path</returns>
    public static object? GetValue(ICkCacheService ckCacheService, string tenantId, RtTypeWithAttributes root,
        IEnumerable<PathTerm> path,
        AttributeValueResolveFlags attributeValueResolveFlags = AttributeValueResolveFlags.Default)
    {
        return GetValueByPath(ckCacheService, tenantId, root, path.ToList(), attributeValueResolveFlags);
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
    ///  Merges field filters into navigation pairs.
    /// </summary>
    /// <remarks>
    /// Field filters are updated when a merge is successful, and the field filter is removed from the collection.
    /// </remarks>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="ckTypeId">Construction kit type id the association belongs to</param>
    /// <param name="navigationPairs">Navigation pairs to be merged with field filters</param>
    /// <param name="fieldFilters">> Field filters to be merged into navigation pairs</param>
    public static void MergeFieldFilterToNavigationPairs(ICkCacheService ckCacheService, string tenantId,
        CkId<CkTypeId> ckTypeId, ICollection<NavigationPair> navigationPairs,
        ICollection<FieldFilter> fieldFilters)
    {
        foreach (FieldFilter fieldFilter in fieldFilters.ToArray())
        {
            var candidate = TokenizeAndGetNavigationPair(ckCacheService, tenantId, ckTypeId,
                fieldFilter.AttributePath);
            if (candidate != null)
            {
                var navigationPair = navigationPairs.FirstOrDefault(np =>
                    np.CkRoleId == candidate.CkRoleId && np.TargetCkTypeId == candidate.TargetCkTypeId);
                if (navigationPair == null)
                {
                    throw InvalidPathException.CannotMergeFieldFilterToNavigationPair(
                        fieldFilter.AttributePath, ckTypeId, candidate.CkRoleId, candidate.TargetCkTypeId);
                }

                var innerCandidate = candidate.InnerNavigationPairs.FirstOrDefault();
                if (innerCandidate != null)
                {
                    var tmpInnerCandidate = innerCandidate;
                    while (tmpInnerCandidate != null)
                    {
                        navigationPair = navigationPair.InnerNavigationPairs.FirstOrDefault(inp =>
                            inp.CkRoleId == tmpInnerCandidate.CkRoleId &&
                            inp.TargetCkTypeId == tmpInnerCandidate.TargetCkTypeId);
                        if (navigationPair == null)
                        {
                            throw InvalidPathException.CannotMergeFieldFilterToNavigationPair(
                                fieldFilter.AttributePath, ckTypeId, tmpInnerCandidate.CkRoleId,
                                tmpInnerCandidate.TargetCkTypeId);
                        }

                        innerCandidate = tmpInnerCandidate;
                        tmpInnerCandidate = tmpInnerCandidate.InnerNavigationPairs.FirstOrDefault();
                    }
                }
                else
                {
                    innerCandidate = candidate;
                }

                if (innerCandidate == null)
                {
                    throw InvalidPathException.CannotMergeFieldFilterToNavigationPairInvalidPath(
                        fieldFilter.AttributePath, ckTypeId);
                }

                navigationPair.AddFieldFilter(GetPath(innerCandidate.SubPathTerms.First()),
                    fieldFilter.Operator, fieldFilter.ComparisonValue);
                fieldFilters.Remove(fieldFilter);
            }
        }
    }

    /// <summary>
    /// Tokenizes a list of paths into a list of traversal navigation pairs
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="rtCkTypeId">Runtime construction kit type id the association belongs to</param>
    /// <param name="paths">List of paths to be evaluated</param>
    /// <returns>A list of navigation pairs, an empty list is returned if no navigation property has been used</returns>
    public static List<NavigationPair> TokenizeAndGetNavigationPairsByRtCkId(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId,
        IEnumerable<string> paths)
    {
        var ckTypeGraph = ckCacheService.GetRtCkType(tenantId, rtCkTypeId);
        return TokenizeAndGetNavigationPairs(ckCacheService, tenantId, ckTypeGraph.CkTypeId, paths);
    }

    /// <summary>
    /// Tokenizes a list of paths and field filters into navigation pairs.
    /// Association meta field filters (paths containing ::) are extracted from <paramref name="fieldFilters"/>,
    /// their paths are tokenized into navigation pairs, and the filter operator/value is applied
    /// as <see cref="AssociationCountFilter"/> on the resulting <see cref="NavigationPair"/>.
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="rtCkTypeId">Runtime construction kit type id the association belongs to</param>
    /// <param name="paths">List of column paths to be evaluated</param>
    /// <param name="fieldFilters">Mutable list of field filters. Association meta filters (::) are removed from
    /// this list after processing and applied as AssociationCountFilter on the navigation pairs.</param>
    /// <returns>A list of navigation pairs including those from association meta filters</returns>
    public static List<NavigationPair> TokenizeAndGetNavigationPairsByRtCkId(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId,
        IEnumerable<string> paths,
        ICollection<FieldFilter> fieldFilters)
    {
        // Extract association meta filters (::) from field filters
        var associationMetaFilters = fieldFilters.Where(ff => ff.AttributePath.Contains("::")).ToList();
        foreach (var filter in associationMetaFilters)
        {
            fieldFilters.Remove(filter);
        }

        // Tokenize column paths and regular field filter paths together
        var regularFieldFilterPaths = fieldFilters.Select(ff => ff.AttributePath);
        var associationMetaPaths = associationMetaFilters.Select(ff => ff.AttributePath);
        var allPaths = paths.Concat(regularFieldFilterPaths).Concat(associationMetaPaths);

        var ckTypeGraph = ckCacheService.GetRtCkType(tenantId, rtCkTypeId);
        var navigationPairs = TokenizeAndGetNavigationPairs(ckCacheService, tenantId, ckTypeGraph.CkTypeId, allPaths);

        // Override default AssociationCountFilter with actual field filter values
        foreach (var filter in associationMetaFilters)
        {
            var navPair = navigationPairs.FirstOrDefault(np => np.AssociationCountFilter != null &&
                filter.AttributePath.StartsWith(
                    np.PathTerms.First(p => p.Type == PathType.Navigation).Value.ToCamelCase() + "."));

            Console.WriteLine($"[RtPathEvaluator Override] filter={filter.AttributePath}, navPairFound={navPair != null}, " +
                              $"navPairs={navigationPairs.Count}, withCountFilter={navigationPairs.Count(np => np.AssociationCountFilter != null)}");
            if (navPair != null)
            {
                Console.WriteLine($"[RtPathEvaluator Override] BEFORE: {navPair.AssociationCountFilter?.Operator} {navPair.AssociationCountFilter?.ComparisonValue}");
            }
            else
            {
                foreach (var np in navigationPairs)
                {
                    var hasNav = np.PathTerms.Any(p => p.Type == PathType.Navigation);
                    var navVal = hasNav ? np.PathTerms.First(p => p.Type == PathType.Navigation).Value : "NONE";
                    Console.WriteLine($"[RtPathEvaluator Override] navPair: nav={navVal}, countFilter={np.AssociationCountFilter != null}, dir={np.Direction}");
                }
            }

            if (navPair == null) continue;

            if (filter.AttributePath.EndsWith("exists"))
            {
                bool wantsExists;
                try
                {
                    wantsExists = Convert.ToBoolean(filter.ComparisonValue);
                }
                catch
                {
                    wantsExists = bool.TryParse(filter.ComparisonValue?.ToString(), out var parsed) && parsed;
                }

                navPair.AssociationCountFilter = filter.Operator switch
                {
                    FieldFilterOperator.Equals => wantsExists
                        ? new AssociationCountFilter(FieldFilterOperator.GreaterEqualThan, 1)
                        : new AssociationCountFilter(FieldFilterOperator.Equals, 0),
                    FieldFilterOperator.NotEquals => wantsExists
                        ? new AssociationCountFilter(FieldFilterOperator.Equals, 0)
                        : new AssociationCountFilter(FieldFilterOperator.GreaterEqualThan, 1),
                    _ => navPair.AssociationCountFilter
                };
            }
            else // totalCount
            {
                navPair.AssociationCountFilter = new AssociationCountFilter(
                    (FieldFilterOperator)(int)filter.Operator,
                    Convert.ToInt32(filter.ComparisonValue));
            }
        }

        return navigationPairs;
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
        List<NavigationPair> navigationPairs = [];
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
    /// <param name="rtCkTypeId">Runtime construction kit type id the association belongs to</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>If navigation is used, the corresponding navigation pair is returned</returns>
    public static NavigationPair? TokenizeAndGetNavigationPairByRtCkId(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId, IEnumerable<PathTerm> path)
    {
        var ckTypeGraph = ckCacheService.GetRtCkType(tenantId, rtCkTypeId);
        return TokenizeAndGetNavigationPair(ckCacheService, tenantId, ckTypeGraph.CkTypeId, path);
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
        CkId<CkTypeId> ckTypeId, IEnumerable<PathTerm> path)
    {
        NavigationPair? navigationPair = null;
        NavigationPair? currentNavigationPair = null;

        var tokens = path.ToList();

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
                    throw InvalidPathException.InvalidPathTermTargetCkTypeIdMissing(tokens, currentToken);
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

            // The reachable end of an inbound navigation is the association's ORIGIN type
            // (the type that declares the association) — the In graph's TargetCkTypeId points
            // at the type the graph is attached to, not at the other end.
            var inAssociations = ckTypeGraph.Associations.In.All
                .Where(a => a.NavigationPropertyName == navigationProperty.Value.ToPascalCase() &&
                            ckCacheService.GetCkType(tenantId, a.OriginCkTypeId).GetAllDerivedTypes(true)
                                .Select(t => t.ToRtCkId().GetTypeName()).Contains(targetTypeProperty.Value)).ToList();
            var outAssociations = ckTypeGraph.Associations.Out.All
                .Where(a => a.NavigationPropertyName == navigationProperty.Value.ToPascalCase() &&
                            ckCacheService.GetCkType(tenantId, a.TargetCkTypeId).GetAllDerivedTypes(true)
                                .Select(t => t.ToRtCkId().GetTypeName()).Contains(targetTypeProperty.Value)).ToList();

            if (inAssociations.Count == 0 && outAssociations.Count == 0)
            {
                throw InvalidPathException.AssociationNotFound(tokens, navigationProperty, targetTypeProperty);
            }

            var entitySelector = TryGetEntitySelector(tokens, targetTypeProperty);

            foreach (var association in inAssociations)
            {
                var realTargetCkTypeId = ckCacheService.GetCkType(tenantId, association.OriginCkTypeId)
                    .GetAllDerivedTypes(true)
                    .First(t => t.ToRtCkId().GetTypeName() == targetTypeProperty.Value);
                ckTypeGraph = ckCacheService.GetCkType(tenantId, realTargetCkTypeId);

                var pathTerms = tokens.TakeWhile(t => t != targetTypeProperty).ToList();
                pathTerms.Add(targetTypeProperty);
                var roleIdDirectionPair = new NavigationPair(pathTerms,
                    [tokens.SkipWhile(t => t != targetTypeProperty).Skip(1)], association.CkRoleId.ToRtCkId(),
                    GraphDirections.Inbound,
                    realTargetCkTypeId.ToRtCkId())
                {
                    EntitySelector = entitySelector
                };
                AddEntitySelectorFieldFilter(roleIdDirectionPair);

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
                    .First(t => t.ToRtCkId().GetTypeName() == targetTypeProperty.Value);
                ckTypeGraph = ckCacheService.GetCkType(tenantId, realTargetCkTypeId);

                var pathTerms = tokens.TakeWhile(t => t != targetTypeProperty).ToList();
                pathTerms.Add(targetTypeProperty);
                var roleIdDirectionPair = new NavigationPair(pathTerms,
                    [tokens.SkipWhile(t => t != targetTypeProperty).Skip(1)], association.CkRoleId.ToRtCkId(),
                    GraphDirections.Outbound,
                    realTargetCkTypeId.ToRtCkId())
                {
                    EntitySelector = entitySelector
                };
                AddEntitySelectorFieldFilter(roleIdDirectionPair);

                if (currentNavigationPair != null)
                {
                    currentNavigationPair.InnerNavigationPairs.Add(roleIdDirectionPair);
                }

                currentNavigationPair = roleIdDirectionPair;
            }

            navigationPair ??= currentNavigationPair;
        }

        // If the path contains an AssociationMeta token (e.g., ::totalCount or ::exists),
        // mark the navigation pair so it will be used for enrichment (loading association counts).
        // Do NOT set a count filter here — filters are only applied from explicit FieldFilters
        // in the TokenizeAndGetNavigationPairsByRtCkId overload that accepts fieldFilters.
        // Setting a default filter here would create separate pipeline stages for each column path.
        var metaToken = tokens.LastOrDefault(t => t.Type == PathType.AssociationMeta);
        if (metaToken != null && navigationPair != null && navigationPair.AssociationCountFilter == null)
        {
            // Mark as association meta navigation with a permissive default (count >= 0 = include all).
            // This ensures the navigation pair triggers enrichment without filtering.
            navigationPair.AssociationCountFilter = new AssociationCountFilter(FieldFilterOperator.GreaterEqualThan, 0);
        }

        return navigationPair;
    }

    /// <summary>
    /// Mirrors the pair's entity selector as a field filter so the MongoDB inner lookup
    /// narrows the loaded target entities server-side (the walker additionally applies the
    /// selector in memory for ends loaded through other pairs).
    /// </summary>
    private static void AddEntitySelectorFieldFilter(NavigationPair pair)
    {
        if (pair.EntitySelector is not { } selector)
        {
            return;
        }

        switch (selector.Kind)
        {
            case NavigationEntitySelectorKind.RtId:
                object comparisonValue = OctoObjectId.TryParse(selector.Value, out var rtId)
                    ? rtId
                    : selector.Value;
                pair.AddFieldFilter("RtId", FieldFilterOperator.Equals, comparisonValue);
                break;
            case NavigationEntitySelectorKind.WellKnownName:
                pair.AddFieldFilter("RtWellKnownName", FieldFilterOperator.Equals, selector.Value);
                break;
            case NavigationEntitySelectorKind.Attribute:
                pair.AddFieldFilter(selector.AttributeName!, FieldFilterOperator.Equals, selector.Value);
                break;
        }
    }

    /// <summary>
    /// Returns the entity selector following the given target type term, if present.
    /// </summary>
    private static NavigationEntitySelector? TryGetEntitySelector(List<PathTerm> tokens, PathTerm targetTypeProperty)
    {
        var index = tokens.IndexOf(targetTypeProperty);
        if (index < 0 || index + 1 >= tokens.Count || tokens[index + 1].Type != PathType.EntitySelector)
        {
            return null;
        }

        return ParseEntitySelector(tokens[index + 1].Value);
    }

    /// <summary>
    /// Parses the raw <c>key=value</c> payload of an entity selector path term
    /// (<c>[rtId=...]</c>, <c>[wellKnownName=...]</c> or <c>[attributeName=value]</c>).
    /// Values may be quoted with single or double quotes; quotes are stripped.
    /// </summary>
    public static NavigationEntitySelector ParseEntitySelector(string rawSelector)
    {
        var separatorIndex = rawSelector.IndexOf('=');
        if (separatorIndex <= 0)
        {
            throw InvalidPathException.InvalidPathTerm(rawSelector);
        }

        var key = rawSelector.Substring(0, separatorIndex).Trim();
        var value = rawSelector.Substring(separatorIndex + 1).Trim().Trim('\'', '"');

        return key.ToLowerInvariant() switch
        {
            "rtid" => new NavigationEntitySelector(NavigationEntitySelectorKind.RtId, null, value),
            "wellknownname" or "rtwellknownname" => new NavigationEntitySelector(
                NavigationEntitySelectorKind.WellKnownName, null, value),
            _ => new NavigationEntitySelector(NavigationEntitySelectorKind.Attribute, key.ToPascalCase(), value)
        };
    }

    /// <summary>
    /// Tokenizes a path into a traversable navigation pair
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">Tenant id</param>
    /// <param name="rtCkTypeId">Construction kit type id the association belongs to</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>If navigation is used, the corresponding navigation pair is returned</returns>
    public static NavigationPair? TokenizeAndGetNavigationPairByRtCkId(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId, string path)
    {
        var tokens = TokenizePath(path);
        return TokenizeAndGetNavigationPairByRtCkId(ckCacheService, tenantId, rtCkTypeId, tokens);
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
        var tokens = TokenizePath(path);
        return TokenizeAndGetNavigationPair(ckCacheService, tenantId, ckTypeId, tokens);
    }

    private static object? GetValueByPath(ICkCacheService ckCacheService, string tenantId,
        RtTypeWithAttributes rtTypeWithAttributes, List<PathTerm> tokens,
        AttributeValueResolveFlags attributeValueResolveFlags)
    {
        var evaluatedPath = MapPath(ckCacheService, tenantId, rtTypeWithAttributes, tokens, attributeValueResolveFlags);

        var pathTuple = evaluatedPath.Last();

        List<object?> results = [];
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
        var evaluatedPath = MapPath(ckCacheService, tenantId, rtTypeWithAttributes, tokens,
            AttributeValueResolveFlags.Default);

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
                                CkRecordId = tupleLocator.CkTypeAttributeGraph.ValueCkRecordId.ToRtCkId(),
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
                pathTuple.Term.Value == nameof(RtEntity.CkTypeId) ||
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
                        // No index specified: replace the entire array value.
                        // Convert to a typed list matching the attribute type to avoid
                        // serialization issues (e.g. JArray not supported by MongoDB BSON).
                        if (setValue is IEnumerable setArray)
                        {
                            object typedList = pathTupleLocator.CkTypeAttributeGraph.ValueType switch
                            {
                                AttributeValueTypesDto.StringArray => setArray.Cast<object?>()
                                    .Select(v => v?.ToString()).ToList(),
                                AttributeValueTypesDto.IntArray => setArray.Cast<object?>()
                                    .Select(v => v != null ? Convert.ToInt32(v) : 0).ToList(),
                                _ => setArray.Cast<object?>().ToList()
                            };

                            pathTupleLocator.RtTypeWithAttributes.SetAttributeValue(
                                pathTupleLocator.CkTypeAttributeGraph.AttributeName,
                                pathTupleLocator.CkTypeAttributeGraph.ValueType,
                                typedList);
                        }
                        else
                        {
                            throw InvalidPathException.PathNotSettable(pathTupleLocator.RtTypeWithAttributes,
                                pathTuple.Term);
                        }

                        break;
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
                case AttributeValueTypesDto.Enum:

                    if (pathTupleLocator.CkTypeAttributeGraph.ValueCkEnumId == null)
                    {
                        throw InvalidPathException.CkEnumIdNotSet(
                            pathTupleLocator.RtTypeWithAttributes, pathTuple.Term);
                    }

                    var ckEnumGraph =
                        ckCacheService.GetCkEnum(tenantId, pathTupleLocator.CkTypeAttributeGraph.ValueCkEnumId);

                    if (setValue is string strValue)
                    {
                        var enumValue = ckEnumGraph.Values.FirstOrDefault(v =>
                            string.Compare(v.Name, strValue, StringComparison.OrdinalIgnoreCase) == 0);
                        if (enumValue == null)
                        {
                            enumValue = ckEnumGraph.Values.FirstOrDefault(v =>
                                string.Compare(v.Key.ToString(), strValue, StringComparison.OrdinalIgnoreCase) == 0);
                        }

                        if (enumValue == null)
                        {
                            throw CkEnumValueNotFoundException.EnumValueNotFound(
                                pathTupleLocator.CkTypeAttributeGraph.ValueCkEnumId, strValue);
                        }


                        pathTupleLocator.RtTypeWithAttributes.SetAttributeValue(
                            pathTupleLocator.CkTypeAttributeGraph.AttributeName,
                            pathTupleLocator.CkTypeAttributeGraph.ValueType, enumValue.Key);
                        break;
                    }

                    if (setValue is int intValue)
                    {
                        var enumValue = ckEnumGraph.Values.FirstOrDefault(v => v.Key == intValue);
                        if (enumValue == null)
                        {
                            throw CkEnumValueNotFoundException.EnumValueNotFound(
                                pathTupleLocator.CkTypeAttributeGraph.ValueCkEnumId, intValue);
                        }

                        pathTupleLocator.RtTypeWithAttributes.SetAttributeValue(
                            pathTupleLocator.CkTypeAttributeGraph.AttributeName,
                            pathTupleLocator.CkTypeAttributeGraph.ValueType, enumValue.Key);
                        break;
                    }

                    throw InvalidPathException.InvalidEnumValueType(
                        pathTupleLocator.CkTypeAttributeGraph.ValueCkEnumId, setValue?.GetType().Name ?? "null");

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
        List<PathTerm> tokens, AttributeValueResolveFlags attributeValueResolveFlags)
    {
        // This list contains the current state of path evaluation (transformation from path to object structure)
        var evaluatedPath = new List<PathTuple>([
            new PathTuple(null, [new PathLocator(rtTypeWithAttributes, null, null, rtTypeWithAttributes)])
        ]);

        var firstMatchNavigation =
            attributeValueResolveFlags.HasFlag(AttributeValueResolveFlags.FirstMatchNavigation);

        // We evaluate the path terms to find the target attribute
        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var nextToken = tokenIndex + 1 < tokens.Count ? tokens[tokenIndex + 1] : null;
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
                                var ckTypeGraph = ckCacheService.GetRtCkType(tenantId, rtEntityGraphItem.CkTypeId);
                                if (ckTypeGraph == null)
                                {
                                    throw InvalidPathException.RtCkTypeIdNotFound(tenantId, rtEntityGraphItem.CkTypeId);
                                }

                                // The reachable end of an outbound navigation is the graph's
                                // TargetCkTypeId; for an inbound navigation it is the
                                // association's ORIGIN type (the In graph's TargetCkTypeId
                                // points at the type the graph is attached to).
                                var candidateNavigations = ckTypeGraph.Associations.Out.All
                                    .Where(x => x.NavigationPropertyName == token.Value.ToPascalCase())
                                    .Select(x => (Graph: x, ReachableCkTypeId: x.TargetCkTypeId))
                                    .ToList();
                                candidateNavigations.AddRange(ckTypeGraph.Associations.In.All
                                    .Where(x => x.NavigationPropertyName == token.Value.ToPascalCase())
                                    .Select(x => (Graph: x, ReachableCkTypeId: x.OriginCkTypeId)));

                                if (candidateNavigations.Count == 0)
                                {
                                    throw InvalidPathException.NavigationPropertyNotFound(tokens, token);
                                }

                                foreach (var candidateNavigation in candidateNavigations)
                                {
                                    navigationEnds.Add(new NavigationEnd
                                    {
                                        RtAssociationRoleId = candidateNavigation.Graph.CkRoleId.ToRtCkId(),
                                        AssociationId = OctoObjectId.Empty,
                                        NavigationPropertyName = candidateNavigation.Graph.NavigationPropertyName,
                                        TargetRtCkTypeId = candidateNavigation.ReachableCkTypeId.ToRtCkId(),
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
                        throw InvalidPathException.InvalidNavigationPropertyToken(locator.RtTypeWithAttributes,
                            token);
                    }
                }
                else if (token.Type == PathType.TargetCkTypeId)
                {
                    if (locator.Value is not List<NavigationEnd> navigationEnds)
                    {
                        throw InvalidPathException.InvalidNavigationPropertyToken(locator.RtTypeWithAttributes,
                            token);
                    }

                    var filteredNavigationEnds = navigationEnds
                        .Where(ne =>
                            ckCacheService.GetRtCkType(tenantId, ne.TargetRtCkTypeId).GetAllDerivedTypes(true)
                                .Select(t => t.ToRtCkId().GetTypeName()).Contains(token.Value)).ToList();

                    if (filteredNavigationEnds.Count == 0)
                    {
                        throw InvalidPathException.TargetCkTypeIdNotFound(tokens, token);
                    }

                    if (filteredNavigationEnds.Count > 1 && !firstMatchNavigation)
                    {
                        throw InvalidPathException.MultipleNavigationEndsUnsupported(locator.RtTypeWithAttributes,
                            token);
                    }

                    // Narrow every matching end to the addressed subtype and collect its targets
                    // of that subtype — an N navigation carries one end per association edge and
                    // may mix target types under one role (e.g. all sensors of a space).
                    var matchingTargets = new List<RtEntityGraphItem>();
                    foreach (var navigationEnd in filteredNavigationEnds)
                    {
                        var addressedCkTypeId = ckCacheService
                            .GetRtCkType(tenantId, navigationEnd.TargetRtCkTypeId)
                            .GetAllDerivedTypes(true).Single(t => t.ToRtCkId().GetTypeName() == token.Value);
                        navigationEnd.TargetRtCkTypeId = addressedCkTypeId.ToRtCkId();

                        var addressedTypeNames = new HashSet<string>(
                            ckCacheService.GetCkType(tenantId, addressedCkTypeId)
                                .GetAllDerivedTypes(true).Select(t => t.ToRtCkId().GetTypeName()),
                            StringComparer.Ordinal);
                        matchingTargets.AddRange(navigationEnd.Targets.Where(t =>
                            t.CkTypeId == null || addressedTypeNames.Contains(t.CkTypeId.GetTypeName())));
                    }

                    if (firstMatchNavigation && matchingTargets.Count > 1)
                    {
                        matchingTargets = matchingTargets
                            .OrderBy(t => t.RtId.ToString(), StringComparer.Ordinal).ToList();

                        // Reduce to the deterministic first match unless an entity selector
                        // follows — the selector must see all candidates to pick from.
                        if (nextToken?.Type != PathType.EntitySelector)
                        {
                            matchingTargets = [matchingTargets[0]];
                        }
                    }

                    if (matchingTargets.Count == 1)
                    {
                        newPathLocators.Add(new PathLocator(matchingTargets[0], null,
                            null, matchingTargets[0]));
                    }
                    else
                    {
                        for (int i = 0; i < matchingTargets.Count; i++)
                        {
                            var target = matchingTargets[i];
                            newPathLocators.Add(new PathLocator(target, null,
                                i, target));
                        }
                    }
                }
                else if (token.Type == PathType.EntitySelector)
                {
                    if (locator.RtTypeWithAttributes is not RtEntityGraphItem targetEntity)
                    {
                        throw InvalidPathException.InvalidTokenType(token);
                    }

                    if (EntitySelectorMatches(targetEntity, ParseEntitySelector(token.Value)))
                    {
                        newPathLocators.Add(new PathLocator(targetEntity, null, locator.Index, targetEntity));
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
                        token.Value.ToPascalCase() == nameof(RtEntity.CkTypeId) ||
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
                        case RtEntity rtEntity:
                        {
                            if (!ckCacheService.TryGetRtCkType(tenantId, rtEntity.GetRtCkTypeId(),
                                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                    out var ckTypeGraph) || ckTypeGraph == null)
                            {
                                throw InvalidPathException.CkTypeNotFoundForEntity(tenantId, rtEntity, token);
                            }

                            if (!ckTypeGraph.AllAttributesByName.TryGetValue(token.Value.ToPascalCase(),
                                    out var ckTypeAttributeGraph))
                            {
                                throw InvalidPathException.AttributeNotFoundOnCkType(tenantId, rtEntity, token);
                            }

                            valueRtTypeWithAttribute.Attributes.TryGetValue(token.Value.ToPascalCase(),
                                out var entityValue);
                            newPathLocators.Add(new PathLocator(rtEntity, ckTypeAttributeGraph, null,
                                ConvertAttributeValue(ckCacheService, tenantId, ckTypeGraph,
                                    token.Value.ToPascalCase(),
                                    entityValue, attributeValueResolveFlags)));
                            continue;
                        }
                        case RtRecord rtRecord:
                        {
                            if (!ckCacheService.TryGetRtCkRecord(tenantId, rtRecord.CkRecordId,
                                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                    out var ckRecordGraph) || ckRecordGraph == null)
                            {
                                throw InvalidPathException.CkRecordNotFoundForRecord(tenantId, rtRecord, token);
                            }

                            if (!ckRecordGraph.AllAttributesByName.TryGetValue(token.Value.ToPascalCase(),
                                    out var ckRecordAttributeGraph))
                            {
                                throw InvalidPathException.AttributeNotFoundOnCkRecord(tenantId, rtRecord, token);
                            }

                            valueRtTypeWithAttribute.Attributes.TryGetValue(token.Value.ToPascalCase(),
                                out var recordValue);
                            newPathLocators.Add(
                                new PathLocator(rtRecord, ckRecordAttributeGraph, null,
                                    ConvertAttributeValue(ckCacheService, tenantId, ckRecordGraph,
                                        token.Value.ToPascalCase(), recordValue, attributeValueResolveFlags)));
                            continue;
                        }
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
                        continue;
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

            // An entity selector may leave several matches (e.g. attribute selector matching
            // more than one target); with first-match semantics reduce deterministically.
            if (token.Type == PathType.EntitySelector && firstMatchNavigation && newPathLocators.Count > 1)
            {
                var firstLocator = newPathLocators
                    .OrderBy(l => (l.RtTypeWithAttributes as RtEntityGraphItem)?.RtId.ToString() ?? string.Empty,
                        StringComparer.Ordinal)
                    .First();
                newPathLocators.Clear();
                newPathLocators.Add(firstLocator);
            }
        }

        return evaluatedPath;
    }

    /// <summary>
    /// Checks whether a navigation target entity matches an entity selector from the path
    /// (<c>[rtId=...]</c>, <c>[wellKnownName=...]</c> or <c>[attributeName=value]</c>).
    /// Attribute values are compared via their invariant string representation.
    /// </summary>
    private static bool EntitySelectorMatches(RtEntityGraphItem entity, NavigationEntitySelector selector)
    {
        switch (selector.Kind)
        {
            case NavigationEntitySelectorKind.RtId:
                return string.Equals(entity.RtId.ToString(), selector.Value, StringComparison.OrdinalIgnoreCase);
            case NavigationEntitySelectorKind.WellKnownName:
                return string.Equals(entity.RtWellKnownName, selector.Value, StringComparison.Ordinal);
            case NavigationEntitySelectorKind.Attribute:
                var attributeValue = entity.GetAttributeValueOrDefault(selector.AttributeName!);
                return attributeValue != null && string.Equals(
                    Convert.ToString(attributeValue, System.Globalization.CultureInfo.InvariantCulture),
                    selector.Value, StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private static object? ConvertAttributeValue(ICkCacheService ckCacheService, string tenantId,
        CkTypeWithAttributesGraph ckTypeWithAttributesGraph, string attributeName, object? value,
        AttributeValueResolveFlags attributeValueResolveFlags)
    {
        if (value == null)
        {
            return null;
        }

        if (!ckTypeWithAttributesGraph.AllAttributesByName.TryGetValue(attributeName, out var ckTypeAttributeGraph))
        {
            throw PersistenceException.AttributeNameNotFound(attributeName, ckTypeWithAttributesGraph);
        }

        switch (ckTypeAttributeGraph.ValueType)
        {
            case AttributeValueTypesDto.Enum:
                if (!attributeValueResolveFlags.HasFlag(AttributeValueResolveFlags.ResolveEnumsToNames))
                {
                    return value;
                }

                // Resolve enum key to name
                if (ckTypeAttributeGraph.ValueCkEnumId == null)
                {
                    throw PersistenceException.CkEnumIdNotSet(attributeName, ckTypeAttributeGraph);
                }

                if (!ckCacheService.TryGetCkEnum(tenantId, ckTypeAttributeGraph.ValueCkEnumId, out var ckEnumGraph) ||
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    ckEnumGraph == null)
                {
                    throw PersistenceException.CkEnumIdNotFound(attributeName, ckTypeAttributeGraph);
                }

                if (value is int intValue)
                {
                    var enumValue = ckEnumGraph.Values.FirstOrDefault(v => v.Key == intValue);
                    if (enumValue == null)
                    {
                        throw PersistenceException.EnumIdValueNotFound(ckTypeAttributeGraph.ValueCkEnumId, intValue);
                    }

                    return enumValue.Name;
                }

                break;
        }

        return value;
    }
}