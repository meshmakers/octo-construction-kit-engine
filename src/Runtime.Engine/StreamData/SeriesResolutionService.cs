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
                IsBase: false)
            {
                // The rung carries the rollup's STORED zone (AB#4190 / O6). The resolution zone the
                // query is answered in (query zone vs. this stored zone) is applied by the grain probe
                // per the comparison policy — see EffectiveGrainMs.
                ReferenceTimeZone = rollup.ReferenceTimeZone,
            });
        }

        return SeriesResolutionPlanner.Plan(
            ladder,
            request.From,
            request.To,
            request.TargetPoints,
            request.RequiredAggregation,
            (rung, from, to) => EffectiveGrainMs(rung, from, to, request.QueryTimeZone, request.ComparisonPolicy));
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
    /// Effective bucket width of a rung over the query window, in the query's resolution zone
    /// (AB#4190 / O6). Fixed-size rungs use their declared grain and are time-zone-independent
    /// (decision T3 — sub-day binning stays UTC; the zone only affects axis labels). Calendar rungs
    /// derive a DST-correct representative width from the wall clock via <see cref="BucketBoundary"/>.
    /// </summary>
    /// <remarks>
    /// The <b>resolution zone</b> a calendar rung must be answered in is:
    /// <list type="bullet">
    /// <item><see cref="SeriesComparisonPolicy.PerQuery"/> ⇒ the query zone
    /// (<paramref name="queryTimeZone"/>), applied uniformly. A calendar rung whose stored
    /// <see cref="ResolutionRung.ReferenceTimeZone"/> resolves to a <em>different</em> zone holds a
    /// different zone's civil buckets, so it cannot answer this query's civil day/week/month — the
    /// probe returns <c>null</c> (indeterminate), which the planner filters out. This is the
    /// zone-match exclusion of decision T3.</item>
    /// <item><see cref="SeriesComparisonPolicy.PerSeries"/> ⇒ the rung's own stored zone; the query
    /// zone is ignored and every calendar rung is eligible.</item>
    /// </list>
    /// </remarks>
    private static long? EffectiveGrainMs(
        ResolutionRung rung, DateTime from, DateTime to,
        string? queryTimeZone, SeriesComparisonPolicy policy)
    {
        if (rung.Alignment == BucketAlignment.FixedSize)
        {
            return rung.GrainMs;
        }

        // Single source of zone resolution (AB#4300) — shared with the write path via BucketBoundary.
        var storedZone = BucketBoundary.ResolveZone(rung.ReferenceTimeZone);

        if (policy == SeriesComparisonPolicy.PerQuery)
        {
            var queryZone = BucketBoundary.ResolveZone(queryTimeZone);
            if (!ZonesEquivalent(storedZone, queryZone))
            {
                // Stored civil buckets are a different zone's — not a valid civil source for this query.
                return null;
            }
        }

        var zone = policy == SeriesComparisonPolicy.PerSeries ? storedZone : BucketBoundary.ResolveZone(queryTimeZone);
        var start = BucketBoundary.AlignDown(from, rung.Alignment, TimeSpan.Zero, zone);
        var end = BucketBoundary.NextBucketEnd(start, rung.Alignment, TimeSpan.Zero, zone);
        var ms = (long)(end - start).TotalMilliseconds;
        return ms > 0 ? ms : null;
    }

    /// <summary>
    /// Two resolution zones are equivalent when they impose the same civil boundaries. <c>null</c>
    /// means UTC on both sides; two named zones match when their adjustment rules coincide (so
    /// <c>Europe/Vienna</c> and <c>Europe/Berlin</c>, which share offset and DST rules, are treated as
    /// the same civil calendar).
    /// </summary>
    private static bool ZonesEquivalent(TimeZoneInfo? a, TimeZoneInfo? b)
    {
        a ??= TimeZoneInfo.Utc;
        b ??= TimeZoneInfo.Utc;
        return a.Equals(b) || a.HasSameRules(b);
    }

    private static long? ToMs(TimeSpan? period) =>
        period is { } p && p > TimeSpan.Zero ? (long)p.TotalMilliseconds : null;

    private static SeriesResolutionResult EmptyLadder(string diagnostic) =>
        new(default, 0, 0, CkRollupFunction.Sum, SeriesResolutionSignal.EmptyLadder) { Diagnostic = diagnostic };
}
