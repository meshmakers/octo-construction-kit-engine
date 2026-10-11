using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The structural rules for the entries of <c>compatibility.acknowledge</c> (AB#6295), shared by the compiler
///     (<c>ckModel.yaml</c>, message 130) and the publish gate (compiled model JSON, <c>OCTO-CK206</c>, AB#6387). The
///     compiled JSON is untrusted input, so the publish gate repeats exactly the checks the compiler makes.
/// </summary>
internal static class CkAcknowledgeEntryRules
{
    private static readonly char[] Wildcards = ['*', '?'];

    /// <summary>
    ///     Returns one problem per violated rule: an empty or wildcard or duplicate key, text that is not a single
    ///     line of plain text, an empty reason. The index is 1-based.
    /// </summary>
    internal static IReadOnlyList<(int Index, string Problem)> FindProblems(IReadOnlyList<CkAcknowledgeDto> entries)
    {
        var problems = new List<(int, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var change = (entry.Change ?? "").Trim();
            var reason = (entry.Reason ?? "").Trim();
            var index = i + 1;
            if (change.Length == 0)
            {
                problems.Add((index, "'change' must not be empty"));
            }
            else if (change.IndexOfAny(Wildcards) >= 0)
            {
                problems.Add((index,
                    $"'change' must be the exact change key printed by the compile gate; wildcards are not allowed ('{change}')"));
            }
            else if (!seen.Add(change))
            {
                problems.Add((index, $"the change '{change}' is acknowledged more than once"));
            }

            if (!IsPlainSingleLine(change) || !IsPlainSingleLine(reason))
            {
                problems.Add((index, "'change' and 'reason' must be a single line of plain text"));
            }

            if (reason.Length == 0)
            {
                problems.Add((index, "'reason' is mandatory and must not be empty"));
            }
        }

        return problems;
    }

    /// <summary>One line of plain text: no control characters (C0/C1, NEL), no line/paragraph separators, no bidi override, no pipeline commands.</summary>
    private static bool IsPlainSingleLine(string text) =>
        !text.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029' or '\u202E' or '\u202D' or '\u2066' or '\u2067' or '\u2068' or '\u2069') &&
        text.IndexOf("##", StringComparison.Ordinal) < 0;
}
