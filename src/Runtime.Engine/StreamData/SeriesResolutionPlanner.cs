using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Pure-function core of the resolution-aware series resolver: given a series' resolution family
/// (its base archive plus rollups) and a query window + target point count, decides which archive to
/// query and at what effective bucket width. No I/O, no time-zone math — the effective grain of each
/// rung is supplied by a caller-provided probe so this class stays deterministic and unit-testable
/// (like <see cref="BucketBoundary"/>). See <c>concept-resolution-aware-series-queries.md</c> §4.2.
/// </summary>
internal static class SeriesResolutionPlanner
{
    /// <summary>
    /// Chooses the archive to query for a reduced-resolution series view.
    /// </summary>
    /// <param name="ladder">
    /// The resolution family: exactly one base rung (<see cref="ResolutionRung.IsBase"/>) plus zero or
    /// more rollup rungs. Order is irrelevant.
    /// </param>
    /// <param name="from">Inclusive window start.</param>
    /// <param name="to">Exclusive window end.</param>
    /// <param name="targetPoints">Desired output point count; must be positive.</param>
    /// <param name="requiredAggregation">
    /// The caller-supplied aggregation semantics (decision O2). A rollup rung is a valid source only
    /// when its stored function equals this.
    /// </param>
    /// <param name="effectiveGrainMs">
    /// Probe returning a rung's effective bucket width in milliseconds over <paramref name="from"/>..
    /// <paramref name="to"/> (fixed rungs → their grain; calendar rungs → a wall-clock/zone-derived
    /// width), or <c>null</c> when the rung's grain is indeterminate (raw / advisory-null Period).
    /// </param>
    public static SeriesResolutionResult Plan(
        IReadOnlyList<ResolutionRung> ladder,
        DateTime from,
        DateTime to,
        int targetPoints,
        CkRollupFunction requiredAggregation,
        Func<ResolutionRung, DateTime, DateTime, long?> effectiveGrainMs)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(effectiveGrainMs);
        if (targetPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPoints), targetPoints, "TargetPoints must be positive.");
        }

        var spanMs = Math.Max(1L, (long)(to - from).TotalMilliseconds);
        var idealBucketMs = Math.Max(1L, spanMs / targetPoints);

        var baseRung = ladder.FirstOrDefault(r => r.IsBase);

        // Eligible reduction sources: rollups whose stored function matches the requested aggregation
        // and whose effective grain is determinate.
        var eligible = ladder
            .Where(r => !r.IsBase && r.StoredFunctionForSeries == requiredAggregation)
            .Select(r => (Rung: r, Grain: effectiveGrainMs(r, from, to)))
            .Where(x => x.Grain is > 0)
            .Select(x => (x.Rung, Grain: x.Grain!.Value))
            .ToList();

        // 1. A compatible rollup fine enough to hit the target → pick the COARSEST such (least scan).
        var fineEnough = eligible.Where(x => x.Grain <= idealBucketMs).ToList();
        if (fineEnough.Count > 0)
        {
            var chosen = fineEnough.OrderByDescending(x => x.Grain).First();
            return new SeriesResolutionResult(
                chosen.Rung.ArchiveRtId, idealBucketMs, targetPoints, requiredAggregation,
                SeriesResolutionSignal.Ok);
        }

        // 2. Compatible rollups exist but all are coarser than ideal → deliver the finest one's native
        //    buckets (fewer points). Do NOT fall through to a finer, costlier source (decision O4).
        if (eligible.Count > 0)
        {
            var finest = eligible.OrderBy(x => x.Grain).First();
            var actual = (int)Math.Max(1L, spanMs / finest.Grain);
            return new SeriesResolutionResult(
                finest.Rung.ArchiveRtId, finest.Grain, actual, requiredAggregation,
                SeriesResolutionSignal.ResolutionLimited)
            {
                ActualPoints = actual,
                Diagnostic =
                    $"Coarsest available {requiredAggregation} rollup grain {finest.Grain} ms exceeds the ideal "
                    + $"{idealBucketMs} ms; delivering {actual} of {targetPoints} points.",
            };
        }

        // 3. No compatible rollup. The base archive is never reduced by the resolver (decision
        //    O2-followup) — decide how to return it.
        if (baseRung is null)
        {
            return new SeriesResolutionResult(
                default, 0, 0, requiredAggregation, SeriesResolutionSignal.EmptyLadder)
            {
                Diagnostic = "No base archive and no compatible rollups resolvable for the request.",
            };
        }

        var baseGrain = effectiveGrainMs(baseRung, from, to);
        if (baseGrain is not > 0)
        {
            // Base grain not declared → cannot tell whether reduction is even needed.
            return new SeriesResolutionResult(
                baseRung.ArchiveRtId, 0, 0, requiredAggregation, SeriesResolutionSignal.UnknownBaseGrain)
            {
                Diagnostic =
                    "No compatible rollup and the base archive grain is not declared; "
                    + "returning the base archive for a direct query.",
            };
        }

        var baseNative = (int)Math.Max(1L, spanMs / baseGrain.Value);
        if (baseNative <= targetPoints)
        {
            // The raw data already fits within the target — no reduction needed, return the base as-is.
            return new SeriesResolutionResult(
                baseRung.ArchiveRtId, baseGrain.Value, baseNative, requiredAggregation,
                SeriesResolutionSignal.Ok)
            {
                Diagnostic = $"Base archive native resolution yields {baseNative} points (<= {targetPoints}); no reduction needed.",
            };
        }

        // The base has more points than the target, but no compatible rollup exists to reduce it and
        // the base is not reduced directly (decision O2-followup) → refuse and signal.
        return new SeriesResolutionResult(
            baseRung.ArchiveRtId, baseGrain.Value, baseNative, requiredAggregation,
            SeriesResolutionSignal.NoSuitableRollup)
        {
            ActualPoints = baseNative,
            Diagnostic =
                $"No {requiredAggregation} rollup available; the base archive would return {baseNative} points "
                + $"(> {targetPoints}). Provision a matching rollup or query raw explicitly.",
        };
    }
}
