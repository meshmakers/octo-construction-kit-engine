using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Assembles a series' resolution family (base archive + its rollups) and delegates the routing
/// decision to the pure <see cref="SeriesResolutionPlanner"/>. Constructed per-tenant with the
/// tenant's stores. See <c>concept-resolution-aware-series-queries.md</c> §5.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 scope (AB#4290): function-matching covers <b>single-step</b> rollups (a rollup whose
/// source is the base archive). A <b>cascade</b> rollup (rollup-of-rollup) has its stored function
/// treated as unknown, so it is conservatively excluded from selection — the resolver may fall back
/// to a direct rollup or signal <see cref="SeriesResolutionSignal.NoSuitableRollup"/>, but never
/// reduces with the wrong function. Cascade function-matching (which needs the storage-column
/// reverse-walk that lives in the CrateDb layer) is a documented follow-up.
/// </para>
/// <para>
/// The base archive is identified by <see cref="SeriesResolutionRequest.BaseArchiveRtId"/>;
/// resolving it from <see cref="SeriesResolutionRequest.TargetCkTypeId"/> alone is a follow-up.
/// </para>
/// </remarks>
public sealed class SeriesResolutionService : ISeriesResolutionService
{
    private readonly IArchiveRuntimeStore _archiveStore;
    private readonly IRollupDependencyGraph _dependencyGraph;

    /// <summary>
    /// Creates the resolver for one tenant from that tenant's archive store and rollup dependency
    /// graph.
    /// </summary>
    public SeriesResolutionService(
        IArchiveRuntimeStore archiveStore,
        IRollupDependencyGraph dependencyGraph)
    {
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
        _dependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
    }

    /// <inheritdoc />
    public async Task<SeriesResolutionResult> ResolveAsync(
        SeriesResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.BaseArchiveRtId is not { } baseRtId)
        {
            return EmptyLadder("No BaseArchiveRtId supplied; resolving from TargetCkTypeId alone is not yet supported.");
        }

        var baseArchive = await _archiveStore.GetAsync(baseRtId).ConfigureAwait(false);
        if (baseArchive is null)
        {
            return EmptyLadder($"Base archive {baseRtId} not found.");
        }

        var ladder = new List<ResolutionRung>
        {
            // The base rung is never reduced by the resolver (decision O2-followup). Its grain is the
            // advisory Period (O5) — null for a raw archive or an undeclared time-range period.
            new(
                baseRtId,
                ToMs(baseArchive.Period),
                BucketAlignment.FixedSize,
                StoredFunctionForSeries: null,
                IsBase: true),
        };

        var rollups = await _dependencyGraph.GetTransitiveDependentsAsync(baseRtId).ConfigureAwait(false);
        foreach (var rollup in rollups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ladder.Add(new ResolutionRung(
                rollup.RtId,
                (long)rollup.BucketSize.TotalMilliseconds,
                rollup.BucketAlignment,
                StoredFunctionForSeries: MatchStoredFunction(rollup, baseRtId, request.SourcePath),
                IsBase: false));
        }

        return SeriesResolutionPlanner.Plan(
            ladder,
            request.From,
            request.To,
            request.TargetPoints,
            request.RequiredAggregation,
            EffectiveGrainMs);
    }

    /// <summary>
    /// Single-step function-matching: a rollup sourced directly from the base archive stores the
    /// aggregation the operator declared for the requested path. A rollup sourced from another rollup
    /// (cascade) is not matched in Phase 1 — its function is reported as unknown (null).
    /// </summary>
    private static CkRollupFunction? MatchStoredFunction(
        RollupArchiveSnapshot rollup, OctoObjectId baseRtId, string sourcePath)
    {
        if (rollup.SourceArchiveRtId != baseRtId)
        {
            return null; // cascade rollup — deferred (see class remarks)
        }

        foreach (var spec in rollup.Aggregations)
        {
            if (string.Equals(spec.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return spec.Function;
            }
        }

        return null; // this rollup does not aggregate the requested path
    }

    /// <summary>
    /// Effective bucket width of a rung over the query window. Fixed rungs use their declared grain;
    /// calendar rungs derive a representative width from the wall clock via <see cref="BucketBoundary"/>
    /// (UTC in Phase 1; reference-time-zone alignment lands in Phase 4 / O6).
    /// </summary>
    private static long? EffectiveGrainMs(ResolutionRung rung, DateTime from, DateTime to)
    {
        if (rung.Alignment == BucketAlignment.FixedSize)
        {
            return rung.GrainMs;
        }

        // Single source of zone resolution (AB#4300) — shared with the write path via BucketBoundary.
        var zone = BucketBoundary.ResolveZone(rung.ReferenceTimeZone);
        var start = BucketBoundary.AlignDown(from, rung.Alignment, TimeSpan.Zero, zone);
        var end = BucketBoundary.NextBucketEnd(start, rung.Alignment, TimeSpan.Zero, zone);
        var ms = (long)(end - start).TotalMilliseconds;
        return ms > 0 ? ms : null;
    }

    private static long? ToMs(TimeSpan? period) =>
        period is { } p && p > TimeSpan.Zero ? (long)p.TotalMilliseconds : null;

    private static SeriesResolutionResult EmptyLadder(string diagnostic) =>
        new(default, 0, 0, CkRollupFunction.Sum, SeriesResolutionSignal.EmptyLadder) { Diagnostic = diagnostic };
}
