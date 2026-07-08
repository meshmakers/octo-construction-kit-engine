using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// DB-neutral chain walker for the resolution-aware series resolver: recovers which logical
/// (source-path, function) tuples a rollup — including a <em>cascade</em> rollup
/// (rollup-of-rollup) — ultimately materialises. Lifts the Phase-1 limitation where cascade
/// rollups were conservatively excluded from series selection (AB#4290 remarks in
/// <see cref="SeriesResolutionService"/>; AB#4336 follow-up).
/// </summary>
/// <remarks>
/// The walk mirrors the CrateDb-side <c>RollupChainAggregationResolver</c> using only
/// contract-level building blocks: column names come from
/// <see cref="RollupColumnGenerator.TargetColumnNamesFor"/> (the same naming rule the DDL and
/// the orchestrator SQL use) and the single-step chain rules are the concept-time-range §7
/// table — pair slots (AVG's sum/count, TWA's integral/duration) accumulate via SUM, COUNT and
/// StateDuration chain via SUM, MIN/MAX only via themselves. A pair function counts as stored
/// only when <em>both</em> slots survive the chain — a lone numerator cannot be recombined.
/// </remarks>
internal static class RollupLadderFunctionResolver
{
    /// <summary>Defends against store inconsistency; well-formed ladders are 1–4 levels.</summary>
    private const int MaxChainDepth = 8;

    private enum PairRole
    {
        Numerator,
        Denominator,
    }

    private readonly record struct Origin(
        string LogicalPath,
        CkRollupFunction Function,
        string ColumnName,
        PairRole? Role);

    /// <summary>
    /// Returns every aggregation function <paramref name="rollup"/> stores for the logical
    /// <paramref name="sourcePath"/> — walking through intermediate rollups in
    /// <paramref name="ladderByRtId"/> down to the base archive
    /// (<paramref name="baseRtId"/>). Empty when the path is not materialised (or the chain is
    /// broken / too deep).
    /// </summary>
    public static IReadOnlyCollection<CkRollupFunction> StoredFunctionsFor(
        RollupArchiveSnapshot rollup,
        OctoObjectId baseRtId,
        string sourcePath,
        IReadOnlyDictionary<OctoObjectId, RollupArchiveSnapshot> ladderByRtId)
    {
        var origins = BuildOrigins(rollup, baseRtId, ladderByRtId, depth: 0);
        if (origins.Count == 0)
        {
            return Array.Empty<CkRollupFunction>();
        }

        var matching = origins
            .Where(o => string.Equals(o.LogicalPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matching.Count == 0)
        {
            return Array.Empty<CkRollupFunction>();
        }

        var functions = new List<CkRollupFunction>();
        foreach (var group in matching.GroupBy(o => o.Function))
        {
            var isPair = group.Key is CkRollupFunction.Avg or CkRollupFunction.TimeWeightedAvg;
            var complete = !isPair
                           || (group.Any(o => o.Role == PairRole.Numerator)
                               && group.Any(o => o.Role == PairRole.Denominator));
            if (complete)
            {
                functions.Add(group.Key);
            }
        }

        return functions;
    }

    private static List<Origin> BuildOrigins(
        RollupArchiveSnapshot rollup,
        OctoObjectId baseRtId,
        IReadOnlyDictionary<OctoObjectId, RollupArchiveSnapshot> ladderByRtId,
        int depth)
    {
        if (depth > MaxChainDepth)
        {
            return new List<Origin>();
        }

        if (rollup.SourceArchiveRtId == baseRtId)
        {
            return BuildDirectOrigins(rollup);
        }

        if (!ladderByRtId.TryGetValue(rollup.SourceArchiveRtId, out var parent))
        {
            // Source is neither the base nor a known ladder member — broken chain (or a source
            // outside this resolution family). Conservatively unmatched.
            return new List<Origin>();
        }

        var parentOrigins = BuildOrigins(parent, baseRtId, ladderByRtId, depth + 1);
        return BuildCascadeOrigins(rollup, parentOrigins);
    }

    private static List<Origin> BuildDirectOrigins(RollupArchiveSnapshot rollup)
    {
        var result = new List<Origin>();
        foreach (var spec in rollup.Aggregations)
        {
            var names = RollupColumnGenerator.TargetColumnNamesFor(spec).ToList();
            if (spec.Function is CkRollupFunction.Avg or CkRollupFunction.TimeWeightedAvg && names.Count == 2)
            {
                result.Add(new Origin(spec.SourcePath, spec.Function, names[0], PairRole.Numerator));
                result.Add(new Origin(spec.SourcePath, spec.Function, names[1], PairRole.Denominator));
            }
            else
            {
                foreach (var name in names)
                {
                    result.Add(new Origin(spec.SourcePath, spec.Function, name, null));
                }
            }
        }

        return result;
    }

    private static List<Origin> BuildCascadeOrigins(
        RollupArchiveSnapshot rollup, List<Origin> parentOrigins)
    {
        var parentByColumn = new Dictionary<string, Origin>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in parentOrigins)
        {
            // First-seen wins — duplicates would indicate an inconsistent parent.
            // (No Dictionary.TryAdd on the netstandard2.0 target.)
            if (!parentByColumn.ContainsKey(o.ColumnName))
            {
                parentByColumn.Add(o.ColumnName, o);
            }
        }

        var result = new List<Origin>();
        foreach (var spec in rollup.Aggregations)
        {
            // The cascade spec's SourcePath is a physical column on the parent (sanitised form).
            if (!parentByColumn.TryGetValue(SanitisePath(spec.SourcePath), out var parent)
                && !parentByColumn.TryGetValue(spec.SourcePath, out parent))
            {
                continue;
            }

            var chained = ChainFunction(parent, spec.Function);
            if (chained is not { } origin)
            {
                continue;
            }

            foreach (var name in RollupColumnGenerator.TargetColumnNamesFor(spec))
            {
                result.Add(new Origin(parent.LogicalPath, origin.Function, name, origin.Role));
            }
        }

        return result;
    }

    /// <summary>
    /// Single-step chain rule (concept-time-range §7): pair slots and additive functions
    /// accumulate via SUM; MIN/MAX only chain via themselves. Null ⇒ not a legal chain.
    /// </summary>
    private static (CkRollupFunction Function, PairRole? Role)? ChainFunction(
        Origin parent, CkRollupFunction applied)
    {
        if (parent.Role is { } role)
        {
            return applied == CkRollupFunction.Sum ? (parent.Function, role) : null;
        }

        return (parent.Function, applied) switch
        {
            (CkRollupFunction.Sum, CkRollupFunction.Sum) => (CkRollupFunction.Sum, null),
            (CkRollupFunction.Count, CkRollupFunction.Sum) => (CkRollupFunction.Count, null),
            (CkRollupFunction.Min, CkRollupFunction.Min) => (CkRollupFunction.Min, null),
            (CkRollupFunction.Max, CkRollupFunction.Max) => (CkRollupFunction.Max, null),
            (CkRollupFunction.StateDuration, CkRollupFunction.Sum) => (CkRollupFunction.StateDuration, null),
            _ => null,
        };
    }

    /// <summary>
    /// Same dot-stripping lower-casing rule as <c>RollupColumnGenerator.SanitisePath</c> /
    /// the CrateDb <c>ColumnNameMapper</c> — a cascade spec references the parent's physical
    /// column, which is the sanitised form of whatever path the parent declared.
    /// </summary>
    private static string SanitisePath(string path)
    {
        var sb = new System.Text.StringBuilder(path.Length);
        foreach (var ch in path)
        {
            if (ch != '.') sb.Append(ch);
        }

        return sb.ToString().ToLowerInvariant();
    }
}
