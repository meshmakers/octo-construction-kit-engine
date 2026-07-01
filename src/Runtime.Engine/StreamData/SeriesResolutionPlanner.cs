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
        if (ladder is null)
        {
            throw new ArgumentNullException(nameof(ladder));
        }

        if (effectiveGrainMs is null)
        {
            throw new ArgumentNullException(nameof(effectiveGrainMs));
        }

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

        // Base-archive native grain (O5): known from Period; null for a raw archive or an undeclared
        // time-range period. When known, baseNative is how many raw points the window already holds.
        var baseGrain = baseRung is null ? null : effectiveGrainMs(baseRung, from, to);
        int? baseNative = baseGrain is > 0 ? (int)Math.Max(1L, spanMs / baseGrain.Value) : null;

        // 1. A compatible rollup fine enough to hit the target → pick the COARSEST such (least scan).
        var fineEnough = eligible.Where(x => x.Grain <= idealBucketMs).ToList();
        if (fineEnough.Count > 0)
        {
            var chosen = fineEnough.OrderByDescending(x => x.Grain).First();
            return new SeriesResolutionResult(
                chosen.Rung.ArchiveRtId, idealBucketMs, targetPoints, requiredAggregation,
                SeriesResolutionSignal.Ok);
        }

        // 2. No rollup is fine enough. If the base archive's native resolution already fits within the
        //    target, return it unreduced (raw fits) — it is finer and delivers more points than any
        //    coarser rollup, so it is preferred over a ResolutionLimited rollup. Checked BEFORE the
        //    ResolutionLimited branch so a short window doesn't get a coarse rollup when the raw data
        //    already fits (e.g. one day of 15-min data = 96 points, not the hourly rollup's 24).
        if (baseRung is not null && baseNative is not null && baseNative.Value <= targetPoints)
        {
            return new SeriesResolutionResult(
                baseRung.ArchiveRtId, baseGrain!.Value, baseNative.Value, requiredAggregation,
                SeriesResolutionSignal.Ok)
            {
                Diagnostic = $"Base archive native resolution yields {baseNative.Value} points (<= {targetPoints}); no reduction needed.",
            };
        }

        // 3. Compatible rollups exist but all are coarser than ideal, and the base does not fit → deliver
        //    the finest rollup's native buckets (fewer points). Do NOT reduce the base directly
        //    (decision O2-followup) or fall through to a finer, costlier source (decision O4).
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

        // 4. No compatible rollup and the base did not fit — decide how to return the base.
        if (baseRung is null)
        {
            return new SeriesResolutionResult(
                default, 0, 0, requiredAggregation, SeriesResolutionSignal.EmptyLadder)
            {
                Diagnostic = "No base archive and no compatible rollups resolvable for the request.",
            };
        }

        if (baseNative is null)
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

        // The base has more points than the target, but no compatible rollup exists to reduce it and
        // the base is not reduced directly (decision O2-followup) → refuse and signal.
        return new SeriesResolutionResult(
            baseRung.ArchiveRtId, baseGrain!.Value, baseNative.Value, requiredAggregation,
            SeriesResolutionSignal.NoSuitableRollup)
        {
            ActualPoints = baseNative.Value,
            Diagnostic =
                $"No {requiredAggregation} rollup available; the base archive would return {baseNative.Value} points "
                + $"(> {targetPoints}). Provision a matching rollup or query raw explicitly.",
        };
    }
}
