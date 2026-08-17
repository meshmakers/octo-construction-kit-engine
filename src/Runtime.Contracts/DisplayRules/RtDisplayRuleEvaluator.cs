using Meshmakers.Octo.ConstructionKit.Contracts.DisplayRules;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.DisplayRules;

/// <summary>
///     Evaluates CK display rules (displayNameRule / displayDescriptionRule) against a runtime
///     entity's attributes. Shared by the save-path modifier, the partial-update recompute and the
///     model-import backfill sweep so the resolution semantics stay identical.
/// </summary>
public static class RtDisplayRuleEvaluator
{
    /// <summary>
    ///     Computes the display value for a rule, or null when the rule is absent, invalid (rules
    ///     are validated at model compile time; a stale invalid rule must not block a save) or
    ///     every referenced attribute is empty.
    /// </summary>
    /// <param name="rule">The effective rule of the entity's CK type</param>
    /// <param name="source">The entity providing the attribute values</param>
    public static string? ComputeValue(string? rule, RtTypeWithAttributes source)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return null;
        }

        var parseResult = DisplayRuleParser.ParseCached(rule!);
        if (!parseResult.IsValid)
        {
            return null;
        }

        return parseResult.Evaluate(path => ResolveAttributePath(source, path));
    }

    /// <summary>
    ///     Resolves a rule path against an entity's attributes; dot-separated segments traverse
    ///     record values (<see cref="RtRecord" />).
    /// </summary>
    public static object? ResolveAttributePath(RtTypeWithAttributes source, string path)
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
