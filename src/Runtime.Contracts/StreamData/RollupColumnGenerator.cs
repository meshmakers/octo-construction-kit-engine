using System;
using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Derives the <see cref="CkArchiveColumnSpec"/> set a rollup archive must materialise to back its
/// <see cref="CkRollupAggregationSpec"/> entries. Pure function, DB-neutral. The output is the same
/// list a raw archive would carry in <see cref="ArchiveSnapshot.Columns"/>, so the existing
/// DDL / query / insert paths can consume a rollup snapshot without branching on rollup-ness.
/// Rollup-archives concept §4.
/// </summary>
/// <remarks>
/// <see cref="CkRollupFunction.Avg"/> materialises as <em>two</em> columns
/// (<c>{base}_sum</c> + <c>{base}_count</c>) so chained rollups stay numerically correct — the
/// average is recomputed on read as <c>sum / NULLIF(count, 0)</c>.
/// <see cref="CkRollupFunction.TimeWeightedAvg"/> follows the same pattern with
/// <c>{base}_integral</c> + <c>{base}_duration</c> (AB#4336); its default base name uses the short
/// token <c>twavg</c>, not the lower-cased enum name. The other functions map 1:1.
/// All generated columns are <c>Indexed = true</c> and <c>Required = false</c>: aggregations can
/// be null for empty buckets and the orchestrator's upsert is the only writer, so a missing
/// value never indicates user error.
/// </remarks>
public static class RollupColumnGenerator
{
    /// <summary>
    /// Generates the column list for a rollup archive from its aggregation specs. The path field
    /// of each emitted <see cref="CkArchiveColumnSpec"/> is the storage column name (already
    /// resolved via the lower-cased <c>{sourcePath}{function}</c> / explicit
    /// <see cref="CkRollupAggregationSpec.TargetColumnName"/> rule).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when two aggregations would produce the same target column name.
    /// </exception>
    public static IReadOnlyList<CkArchiveColumnSpec> Generate(IReadOnlyList<CkRollupAggregationSpec> aggregations)
    {
        if (aggregations is null)
        {
            throw new ArgumentNullException(nameof(aggregations));
        }

        var result = new List<CkArchiveColumnSpec>(aggregations.Count * 2);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var spec in aggregations)
        {
            foreach (var columnName in TargetColumnNamesFor(spec))
            {
                if (!seen.Add(columnName))
                {
                    throw new ArgumentException(
                        $"Duplicate target column '{columnName}' produced by aggregations — pick distinct TargetColumnName values.",
                        nameof(aggregations));
                }
                result.Add(new CkArchiveColumnSpec(columnName, Indexed: true, Required: false));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the target column name(s) produced by one aggregation spec, in stable order. Single
    /// name for MIN/MAX/SUM/COUNT, two names for AVG (<c>{base}_sum</c>, <c>{base}_count</c>).
    /// Exposed so callers that need the same naming convention without instantiating
    /// <see cref="CkArchiveColumnSpec"/> values (e.g. the DDL generator) can share the logic.
    /// </summary>
    public static IEnumerable<string> TargetColumnNamesFor(CkRollupAggregationSpec spec)
    {
        if (string.IsNullOrEmpty(spec.SourcePath))
        {
            throw new ArgumentException("SourcePath must not be empty.", nameof(spec));
        }

        var baseName = !string.IsNullOrWhiteSpace(spec.TargetColumnName)
            ? spec.TargetColumnName!.ToLowerInvariant()
            : DefaultBaseNameFor(spec);

        return spec.Function switch
        {
            CkRollupFunction.Avg => new[] { $"{baseName}_sum", $"{baseName}_count" },
            CkRollupFunction.Min => new[] { baseName },
            CkRollupFunction.Max => new[] { baseName },
            CkRollupFunction.Sum => new[] { baseName },
            CkRollupFunction.Count => new[] { baseName },
            CkRollupFunction.TimeWeightedAvg => new[] { $"{baseName}_integral", $"{baseName}_duration" },
            CkRollupFunction.StateDuration => new[] { baseName },
            CkRollupFunction.First => new[] { baseName },
            CkRollupFunction.Last => new[] { baseName },
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Function, "Unknown rollup function.")
        };
    }

    /// <summary>
    /// The column name this spec would generate if it did not pin one with
    /// <see cref="CkRollupAggregationSpec.TargetColumnName"/> — <c>{sanitised source path}_{function
    /// token}</c>, the base that <see cref="TargetColumnNamesFor"/> derives its one or two names
    /// from.
    /// </summary>
    /// <remarks>
    /// This is the read-side identity of the aggregated quantity: it is determined by what the spec
    /// aggregates (path and function) and not by where it happens to store the result, so two specs
    /// with the same default base name aggregate the same thing. A pinned
    /// <see cref="CkRollupAggregationSpec.TargetColumnName"/> is a storage decision and is
    /// deliberately ignored here — the per-source resolver (AB#5157) uses this to recognise a
    /// physically-chained child rollup, and matching on the stored name instead would accept a child
    /// that aggregates a different attribute under a coincidental column name.
    /// </remarks>
    public static string DefaultBaseNameFor(CkRollupAggregationSpec spec)
    {
        if (string.IsNullOrEmpty(spec.SourcePath))
        {
            throw new ArgumentException("SourcePath must not be empty.", nameof(spec));
        }

        return $"{SanitisePath(spec.SourcePath)}_{FunctionToken(spec.Function)}";
    }

    /// <summary>
    /// Default-name token per function: the lower-cased enum name, except
    /// <see cref="CkRollupFunction.TimeWeightedAvg"/> which uses the short token <c>twavg</c>
    /// (AB#4336 decision D5) so default column names stay readable
    /// (<c>dimminglevel_twavg_integral</c>, not <c>dimminglevel_timeweightedavg_integral</c>).
    /// </summary>
    private static string FunctionToken(CkRollupFunction function)
    {
        return function == CkRollupFunction.TimeWeightedAvg
            ? "twavg"
            : function.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Lower-cases the path and strips dots so dotted attribute paths
    /// (<c>sensor.reading.value</c>) collapse to a CrateDB-safe column name
    /// (<c>sensorreadingvalue</c>). Kept here in Runtime.Contracts so the contract-level helper
    /// and the CrateDB-side <c>ColumnNameMapper.PathToColumnName</c> stay in sync; the latter is
    /// the canonical reference for the actual storage layer. Public because the engine's
    /// per-source aggregation resolver (AB#5157) and the ladder chain walker compare logical
    /// source paths through exactly this mapping; an already-sanitised name is returned unchanged.
    /// </summary>
    /// <param name="path">A logical attribute path or an already-physical column name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    public static string SanitisePath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var sb = new System.Text.StringBuilder(path.Length);
        foreach (var ch in path)
        {
            if (ch != '.') sb.Append(ch);
        }
        return sb.ToString().ToLowerInvariant();
    }
}
