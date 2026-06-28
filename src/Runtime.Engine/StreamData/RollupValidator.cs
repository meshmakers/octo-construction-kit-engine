using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Pure-function validators for the rollup-archive save-time and activation-time rules
/// (rollup-archives concept §10). No I/O — callers fetch the snapshots and pass them in.
/// </summary>
/// <remarks>
/// The save-time checks (<see cref="ValidateForSave"/>) are intentionally cheap so they can run
/// on every Mongo upsert without extra repository hits. The activation-time check
/// (<see cref="ValidateForActivation"/>) re-runs the save-time checks plus the cross-archive ones
/// that need the source snapshot, so the lifecycle service has a single entry point.
/// </remarks>
public static class RollupValidator
{
    /// <summary>
    /// Validates the structural invariants of a rollup snapshot that do not require the source
    /// archive — non-empty aggregations, no duplicate <c>(SourcePath, Function)</c> pairs, no
    /// direct self-cycle.
    /// </summary>
    public static void ValidateForSave(RollupArchiveSnapshot rollup)
    {
        if (rollup.Aggregations is null || rollup.Aggregations.Count == 0)
        {
            throw new RollupAggregationsRequiredException(rollup.RtId);
        }

        if (rollup.SourceArchiveRtId == rollup.RtId)
        {
            // Direct cycle. Transitive cycles (A → B → A) need a graph walk and are checked at
            // activation time once the source can be loaded (concept §10 follow-up).
            throw new RollupCycleException(rollup.RtId);
        }

        var seen = new HashSet<(string Path, CkRollupFunction Function)>();
        foreach (var agg in rollup.Aggregations)
        {
            if (!seen.Add((agg.SourcePath, agg.Function)))
            {
                throw new DuplicateRollupAggregationException(rollup.RtId, agg.SourcePath, agg.Function);
            }
        }
    }

    /// <summary>
    /// Validates everything required to provision the rollup's CrateDB table and start the
    /// orchestrator: the save-time invariants from <see cref="ValidateForSave"/>, plus that
    /// <paramref name="source"/> is non-null, activated, and carries every
    /// <see cref="CkRollupAggregationSpec.SourcePath"/> referenced by the rollup.
    /// </summary>
    /// <param name="rollup">The rollup snapshot being activated.</param>
    /// <param name="source">
    /// The source archive's current snapshot, loaded via
    /// <see cref="IArchiveRuntimeStore.GetAsync"/>. <c>null</c> when the source archive does not
    /// exist (or has been soft-deleted), surfaced via <see cref="RollupSourceMissingException"/>.
    /// </param>
    public static void ValidateForActivation(RollupArchiveSnapshot rollup, ArchiveSnapshot? source)
    {
        ValidateForSave(rollup);

        if (source is null)
        {
            throw new RollupSourceMissingException(rollup.RtId, rollup.SourceArchiveRtId);
        }

        if (source.Status != CkArchiveStatus.Activated)
        {
            throw new RollupSourceNotActivatedException(rollup.RtId, rollup.SourceArchiveRtId, source.Status);
        }

        // A rollup aggregation references a source column by name: an ingested column by its Path,
        // or a computed column by its Name (concept §10 / AB#4189). Both map to the same physical
        // source column the rollup SQL aggregates over (ColumnNameMapper.PathToColumnName), so a
        // rollup can aggregate a computed column exactly like a normal one.
        var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in source.Columns)
        {
            if (column.IsComputed)
            {
                if (!string.IsNullOrWhiteSpace(column.Name))
                {
                    // Guarded above; null-forgiving keeps the netstandard2.0 target (whose
                    // string.IsNullOrWhiteSpace lacks the [NotNullWhen] annotation) warning-clean.
                    sourcePaths.Add(column.Name!);
                }
            }
            else if (!string.IsNullOrWhiteSpace(column.Path))
            {
                sourcePaths.Add(column.Path);
            }
        }

        foreach (var agg in rollup.Aggregations)
        {
            if (!sourcePaths.Contains(agg.SourcePath))
            {
                throw new RollupSourcePathInvalidException(
                    rollup.RtId,
                    agg.SourcePath,
                    $"source archive '{rollup.SourceArchiveRtId}' does not capture this path.");
            }
        }
    }
}
