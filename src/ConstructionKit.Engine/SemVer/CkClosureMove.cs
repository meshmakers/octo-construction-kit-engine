using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     AB#5435 (decision of Thor 2026-10-14): the one predicate that decides whether a change of the resolved dependency
///     closure of a <b>range-retaining</b> model is a change of the model at all. The closure (<c>dependencies</c>) of such
///     a model is compiler-set metadata, like <c>minEngineVersion</c>: its references are major-qualified and bound when it
///     loads, ranges and floors (not the closure) decide what resolves at run time. The classifier
///     (<c>ValidateVersion</c> and the compile gate) and the publish gate's same-version equivalence
///     (<see cref="CkPublishEquivalence" />) both ask here, so they cannot drift.
///     <para>
///         A closure entry <b>moved within its declared range</b> when all of this holds, otherwise the existing rows
///         decide as before: baseline and current are both range-retaining; their declared ranges and floors are
///         identical; the entry exists on both sides, its version went <b>up</b> (a lower version is row D3, Major),
///         stays in the <b>same major</b>, and for a direct dependency the current version satisfies the effective range
///         (range with the floor). Exact-pinned (v1) models never take this path: their references are concrete, a
///         dependency move is a real change until the one-time re-pin (F2.6, AB#5688).
///     </para>
/// </summary>
internal static class CkClosureMove
{
    /// <summary>True when the closure entry <paramref name="name" /> moved within its declared range (see the type remarks).</summary>
    public static bool IsMoveWithinDeclaredRange(CkCompiledModelRoot baseline, CkCompiledModelRoot current, string name,
        CkVersion from, CkVersion to)
    {
        if (!baseline.IsRangeRetaining || !current.IsRangeRetaining || !SameDeclaredRanges(baseline, current))
        {
            return false;
        }

        if (to.Major != from.Major || to.CompareTo(from) <= 0)
        {
            return false;
        }

        var declared = current.DependencyRanges!.FirstOrDefault(d => d.Range.Name == name);
        return declared == null || declared.GetEffectiveRange().IsSatisfiedBy(new CkModelId(name, to.ToString()!));
    }

    /// <summary>
    ///     True when the closures of the two models differ only by moves within the declared ranges and at least one
    ///     entry moved: same set of names, every differing entry a move (<see cref="IsMoveWithinDeclaredRange" />).
    ///     Everything else being equal is the caller's comparison (the equivalence compares the rest of the model).
    /// </summary>
    public static bool IsWithinDeclaredRange(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        if (!baseline.IsRangeRetaining || !current.IsRangeRetaining)
        {
            return false;
        }

        var before = (baseline.Dependencies ?? []).ToDictionary(d => d.Name, d => d.Version, StringComparer.Ordinal);
        var after = (current.Dependencies ?? []).ToDictionary(d => d.Name, d => d.Version, StringComparer.Ordinal);
        if (before.Count != after.Count || before.Keys.Any(k => !after.ContainsKey(k)))
        {
            return false;
        }

        var moved = false;
        foreach (var entry in before)
        {
            var name = entry.Key;
            var from = entry.Value;
            var to = after[name];
            if (from.Equals(to))
            {
                continue;
            }

            if (!IsMoveWithinDeclaredRange(baseline, current, name, from, to))
            {
                return false;
            }

            moved = true;
        }

        return moved;
    }

    private static bool SameDeclaredRanges(CkCompiledModelRoot a, CkCompiledModelRoot b)
    {
        string Key(CkModelDependencyDto d) => $"{d.Range.FullName}|{d.Floor}";
        var left = a.DependencyRanges!.Select(Key).OrderBy(k => k, StringComparer.Ordinal);
        var right = b.DependencyRanges!.Select(Key).OrderBy(k => k, StringComparer.Ordinal);
        return left.SequenceEqual(right);
    }
}
