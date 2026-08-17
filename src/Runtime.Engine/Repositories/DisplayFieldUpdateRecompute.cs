using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.DisplayRules;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Pure logic for the smart display-field recompute on partial updates (AB#4811); the DB
///     round-trip stays in <see cref="BulkRtMutation" />. A partial update touches a display rule
///     when it carries one of the rule's root attributes (records are updated by their root
///     attribute, so record sub-updates count). Evaluation resolves each referenced path from the
///     partial update when it carries the root attribute, otherwise from the stored entity. An
///     evaluation yielding no value is written as an empty string — the update mappers translate
///     that to a clear (null on a partial update document means "not recomputed, leave unchanged").
/// </summary>
internal static class DisplayFieldUpdateRecompute
{
    /// <summary>
    ///     Returns the memoized parse result for a rule, or null when the rule is absent or
    ///     invalid (rules are validated at model compile time; a stale invalid rule must not
    ///     block the update).
    /// </summary>
    internal static DisplayRuleParseResult? GetValidParseResult(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return null;
        }

        var parseResult = DisplayRuleParser.ParseCached(rule!);
        return parseResult.IsValid ? parseResult : null;
    }

    /// <summary>
    ///     Root attributes referenced by the type's display rules (first path segment).
    /// </summary>
    internal static HashSet<string> GetReferencedRootAttributes(DisplayRuleParseResult? nameParseResult,
        DisplayRuleParseResult? descriptionParseResult)
    {
        return new HashSet<string>(
            (nameParseResult?.ReferencedPaths ?? Enumerable.Empty<string>())
            .Concat(descriptionParseResult?.ReferencedPaths ?? Enumerable.Empty<string>())
            .Select(GetRootAttribute));
    }

    /// <summary>
    ///     True when the partial update carries an attribute referenced by the display rules.
    /// </summary>
    internal static bool TouchesReferencedAttributes(RtEntity partialEntity,
        HashSet<string> referencedRootAttributes)
    {
        return partialEntity.Attributes.Keys.Any(referencedRootAttributes.Contains);
    }

    /// <summary>
    ///     Re-evaluates the display rules against stored + updated attributes and stamps the
    ///     result onto the partial update document (empty string = clear sentinel).
    /// </summary>
    internal static void Recompute(DisplayRuleParseResult? nameParseResult,
        DisplayRuleParseResult? descriptionParseResult, RtEntity partialEntity, RtEntity storedEntity)
    {
        object? ResolveValue(string path)
        {
            var source = partialEntity.Attributes.ContainsKey(GetRootAttribute(path))
                ? partialEntity
                : storedEntity;
            return ResolveAttributePath(source, path);
        }

        if (nameParseResult != null)
        {
            partialEntity.RtDisplayName = nameParseResult.Evaluate(ResolveValue) ?? string.Empty;
        }

        if (descriptionParseResult != null)
        {
            partialEntity.RtDisplayDescription = descriptionParseResult.Evaluate(ResolveValue) ?? string.Empty;
        }
    }

    private static string GetRootAttribute(string path)
    {
        var separatorIndex = path.IndexOf('.');
        return separatorIndex < 0 ? path : path.Substring(0, separatorIndex);
    }

    /// <summary>
    ///     Resolves a rule path against an entity's attributes; dot-separated segments traverse
    ///     record values.
    /// </summary>
    private static object? ResolveAttributePath(RtTypeWithAttributes source, string path)
    {
        var current = source;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var value = current.GetAttributeValueOrDefault(segments[i]);
            if (i == segments.Length - 1)
            {
                return value;
            }

            if (value is not RtTypeWithAttributes nested)
            {
                return null;
            }

            current = nested;
        }

        return null;
    }
}
