using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DisplayRules;

/// <summary>
///     Parses display rules declared on CK types (displayNameRule / displayDescriptionRule).
///     A rule is literal text with <c>${attributePath}</c> placeholders using the established
///     OctoMesh interpolation dialect, extended with a <c>??</c> coalesce operator inside a
///     placeholder, e.g. <c>"${roomNumber} - ${name ?? globalId}"</c>. Attribute paths address
///     own attributes of the entity including record paths (<c>${record.field}</c>); association
///     traversal is not supported.
/// </summary>
public static class DisplayRuleParser
{
    private static readonly Regex AttributePathRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.Compiled);

    private static readonly string[] CoalesceSeparator = ["??"];

    /// <summary>
    ///     Parses a display rule into literal and placeholder segments. Syntax errors are collected
    ///     on the result instead of throwing, so callers can report them as model compile messages.
    /// </summary>
    /// <param name="rule">The rule text, e.g. "${roomNumber} - ${name ?? globalId}"</param>
    public static DisplayRuleParseResult Parse(string rule)
    {
        var segments = new List<DisplayRuleSegment>();
        var errors = new List<string>();

        var position = 0;
        while (position < rule.Length)
        {
            var placeholderStart = rule.IndexOf("${", position, StringComparison.Ordinal);
            if (placeholderStart < 0)
            {
                segments.Add(new DisplayRuleLiteralSegment(rule.Substring(position)));
                break;
            }

            if (placeholderStart > position)
            {
                segments.Add(new DisplayRuleLiteralSegment(rule.Substring(position, placeholderStart - position)));
            }

            var placeholderEnd = rule.IndexOf('}', placeholderStart + 2);
            if (placeholderEnd < 0)
            {
                errors.Add($"Unterminated placeholder starting at position {placeholderStart}.");
                break;
            }

            var expression = rule.Substring(placeholderStart + 2, placeholderEnd - placeholderStart - 2);
            var paths = new List<string>();
            foreach (var rawPath in expression.Split(CoalesceSeparator, StringSplitOptions.None))
            {
                var path = rawPath.Trim();
                if (path.Length == 0)
                {
                    errors.Add($"Empty attribute path in placeholder '${{{expression}}}'.");
                    continue;
                }

                if (!AttributePathRegex.IsMatch(path))
                {
                    errors.Add($"Invalid attribute path '{path}' in placeholder '${{{expression}}}'. " +
                               "Expected an attribute name or a dot-separated record path.");
                    continue;
                }

                paths.Add(path);
            }

            if (paths.Count == 0 && errors.Count == 0)
            {
                errors.Add($"Empty placeholder at position {placeholderStart}.");
            }

            if (paths.Count > 0)
            {
                segments.Add(new DisplayRulePlaceholderSegment(paths));
            }

            position = placeholderEnd + 1;
        }

        if (segments.Count == 0 && errors.Count == 0)
        {
            errors.Add("Rule is empty.");
        }

        if (errors.Count == 0 && segments.All(s => s is DisplayRuleLiteralSegment))
        {
            errors.Add("Rule contains no ${attributePath} placeholder.");
        }

        return new DisplayRuleParseResult(segments, errors);
    }
}

/// <summary>
///     Result of parsing a display rule: the segment list, collected syntax errors and the
///     distinct set of referenced attribute paths.
/// </summary>
public sealed class DisplayRuleParseResult
{
    internal DisplayRuleParseResult(IReadOnlyList<DisplayRuleSegment> segments, IReadOnlyList<string> errors)
    {
        Segments = segments;
        Errors = errors;
        ReferencedPaths = segments
            .OfType<DisplayRulePlaceholderSegment>()
            .SelectMany(s => s.Paths)
            .Distinct()
            .ToList();
    }

    /// <summary>
    ///     The parsed literal and placeholder segments in rule order.
    /// </summary>
    public IReadOnlyList<DisplayRuleSegment> Segments { get; }

    /// <summary>
    ///     Syntax errors collected while parsing; empty when the rule is valid.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    ///     True when the rule parsed without errors.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    ///     Distinct attribute paths referenced by the rule, in order of first occurrence.
    ///     Record paths are dot-separated (e.g. "thermalRequirements.spaceTemperature").
    /// </summary>
    public IReadOnlyCollection<string> ReferencedPaths { get; }

    /// <summary>
    ///     Evaluates the rule against attribute values. Each placeholder resolves to the first
    ///     path in its coalesce chain that yields a non-empty value. Returns null when every
    ///     placeholder evaluated empty (the caller then falls back to its own default) or when
    ///     the overall result is blank.
    /// </summary>
    /// <param name="resolveValue">Resolves an attribute path to its current value (null when unset)</param>
    public string? Evaluate(Func<string, object?> resolveValue)
    {
        var builder = new StringBuilder();
        var anyPlaceholderResolved = false;

        foreach (var segment in Segments)
        {
            switch (segment)
            {
                case DisplayRuleLiteralSegment literal:
                    builder.Append(literal.Text);
                    break;
                case DisplayRulePlaceholderSegment placeholder:
                    foreach (var path in placeholder.Paths)
                    {
                        var text = FormatValue(resolveValue(path));
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        builder.Append(text);
                        anyPlaceholderResolved = true;
                        break;
                    }

                    break;
            }
        }

        if (!anyPlaceholderResolved)
        {
            return null;
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string? FormatValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}

/// <summary>
///     Base class for display rule segments.
/// </summary>
public abstract class DisplayRuleSegment;

/// <summary>
///     Literal text between placeholders.
/// </summary>
public sealed class DisplayRuleLiteralSegment : DisplayRuleSegment
{
    internal DisplayRuleLiteralSegment(string text)
    {
        Text = text;
    }

    /// <summary>
    ///     The literal text.
    /// </summary>
    public string Text { get; }
}

/// <summary>
///     A <c>${path ?? path ...}</c> placeholder; the first path yielding a non-empty value wins.
/// </summary>
public sealed class DisplayRulePlaceholderSegment : DisplayRuleSegment
{
    internal DisplayRulePlaceholderSegment(IReadOnlyList<string> paths)
    {
        Paths = paths;
    }

    /// <summary>
    ///     The coalesce chain of attribute paths.
    /// </summary>
    public IReadOnlyList<string> Paths { get; }
}
