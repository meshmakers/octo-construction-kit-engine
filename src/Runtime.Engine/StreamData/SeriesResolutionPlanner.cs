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
/// <remarks>
/// <para>
/// <b>Measured coverage filter (AB#5157).</b> Before the selection rules run, the rungs are
/// filtered by <see cref="ResolutionRung.AvailableFrom"/> — the base rung included:
/// </para>
/// <list type="bullet">
/// <item>When no rung reports coverage the filter is inert and the pre-AB#5157 plan is returned
/// unchanged.</item>
/// <item>Otherwise the candidates are the rungs whose coverage starts at or before the requested
/// start (the requested end is never considered); a rung without coverage never covers. When no
/// rung covers the start, the rung(s) with the earliest available-from are the candidates — ties
/// keep every tied rung.</item>
/// <item>The selection rules run unchanged over the candidates. When they pick the same archive
/// the unfiltered plan is returned as is (same signal, same points). When they pick a different
/// archive and the filtered plan is a real reduction (<see cref="SeriesResolutionSignal.Ok"/> or
/// <see cref="SeriesResolutionSignal.ResolutionLimited"/>) the result carries
/// <see cref="SeriesResolutionSignal.CoverageLimited"/>, <c>ActualPoints</c> = the delivered count,
/// a diagnostic naming the excluded rung and its available-from, and
/// <see cref="SeriesResolutionResult.FinerRungAvailableFrom"/> (null when that rung reports no
/// coverage). A filtered refuse path (<see cref="SeriesResolutionSignal.NoSuitableRollup"/>,
/// <see cref="SeriesResolutionSignal.UnknownBaseGrain"/>) keeps its own truthful signal with the
/// coverage exclusion prepended to the diagnostic; and when the candidates cannot name any archive
/// at all (<see cref="SeriesResolutionSignal.EmptyLadder"/>) the filter changes nothing — a
/// <c>CoverageLimited</c> answer always carries a covering fallback rung.</item>
/// </list>
/// </remarks>
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
    /// when its stored functions for the series include this — a rollup may carry several
    /// aggregations on the same source path (AB#4188).
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

        var unfiltered = PlanCore(ladder, from, to, targetPoints, requiredAggregation, effectiveGrainMs);

        // AB#5157: measured coverage filter. Null ⇒ no rung reports coverage ⇒ inert.
        var candidates = SelectCoveringRungs(ladder, from);
        if (candidates is null || candidates.Count == ladder.Count)
        {
            return unfiltered;
        }

        var filtered = PlanCore(candidates, from, to, targetPoints, requiredAggregation, effectiveGrainMs);
        if (filtered.ArchiveRtId == unfiltered.ArchiveRtId)
        {
            return unfiltered;
        }

        if (filtered.Signal == SeriesResolutionSignal.EmptyLadder)
        {
            // The covering rungs cannot serve the request at all (e.g. only an incompatible
            // function holds data) — there is no covering fallback to redirect to, so the
            // pre-AB#5157 answer stands.
            return unfiltered;
        }

        // The unfiltered choice is, by construction, not among the candidates (were it, the same
        // rules over the subset would have picked it again): it is the rung the coverage filter
        // excluded.
        var excluded = ladder.FirstOrDefault(r => r.ArchiveRtId == unfiltered.ArchiveRtId);
        var exclusion = excluded is null
            ? "Coverage filter excluded the rung the resolver would otherwise have chosen"
            : excluded.AvailableFrom is { } availableFrom
                ? $"Coverage filter: rung {excluded.ArchiveRtId} holds data only from {availableFrom:O}, after the requested start {from:O}"
                : $"Coverage filter: rung {excluded.ArchiveRtId} reports no coverage";

        if (filtered.Signal is not (SeriesResolutionSignal.Ok or SeriesResolutionSignal.ResolutionLimited))
        {
            // A refuse path over the covering rungs stays truthful about the base; only the
            // diagnostic learns why the finer rung was not considered.
            return filtered with
            {
                Diagnostic = filtered.Diagnostic is null ? $"{exclusion}." : $"{exclusion}; {filtered.Diagnostic}",
            };
        }

        // The excluded rung is only worth naming as "finer data starts here" when it actually is
        // finer than the one being delivered. It need not be: the planner prefers whichever rung
        // fits the requested point count best, so a COARSER rung can be the excluded one — and a
        // freshly created coarse rung is precisely the case whose coverage starts latest. Reporting
        // its start as a finer resolution would tell the caller the opposite of the truth.
        var chosen = ladder.FirstOrDefault(r => r.ArchiveRtId == filtered.ArchiveRtId);
        var excludedIsFiner = excluded is not null &&
            IsFinerThan(effectiveGrainMs(excluded, from, to), chosen is null ? null : effectiveGrainMs(chosen, from, to));

        return new SeriesResolutionResult(
            filtered.ArchiveRtId, filtered.EffectiveBucketMs, filtered.Points, filtered.ReducingFunction,
            SeriesResolutionSignal.CoverageLimited)
        {
            ActualPoints = filtered.Points,
            Diagnostic = filtered.Diagnostic is null
                ? $"{exclusion}; delivering {filtered.Points} points from {filtered.ArchiveRtId} instead."
                : $"{exclusion}; delivering {filtered.Points} points from {filtered.ArchiveRtId} instead. {filtered.Diagnostic}",
            FinerRungAvailableFrom = excludedIsFiner ? excluded!.AvailableFrom : null,
        };
    }

    /// <summary>
    /// True when <paramref name="candidateGrain"/> is a finer resolution than
    /// <paramref name="chosenGrain"/>. A <c>null</c> grain means "finest, unknown resolution" (a raw
    /// archive, or a time-range archive without an advisory period), so it beats every known grain
    /// and ties with another unknown one. AB#5157 review.
    /// </summary>
    private static bool IsFinerThan(long? candidateGrain, long? chosenGrain) =>
        candidateGrain is null
            ? chosenGrain is not null
            : chosenGrain is { } chosen && candidateGrain.Value < chosen;

    /// <summary>
    /// The coverage filter (AB#5157): <c>null</c> when no rung reports coverage (inert); otherwise
    /// the rungs whose <see cref="ResolutionRung.AvailableFrom"/> is at or before
    /// <paramref name="from"/>, or — when none is — every rung sharing the earliest available-from.
    /// A rung without coverage is never a candidate once any rung has some.
    /// </summary>
    private static IReadOnlyList<ResolutionRung>? SelectCoveringRungs(IReadOnlyList<ResolutionRung> ladder, DateTime from)
    {
        var withCoverage = ladder.Where(r => r.AvailableFrom is not null).ToList();
        if (withCoverage.Count == 0)
        {
            return null;
        }

        var covering = withCoverage.Where(r => r.AvailableFrom!.Value <= from).ToList();
        if (covering.Count > 0)
        {
            return covering;
        }

        var earliest = withCoverage.Min(r => r.AvailableFrom!.Value);
        return withCoverage.Where(r => r.AvailableFrom!.Value == earliest).ToList();
    }

    /// <summary>
    /// The selection rules 1-4 over <paramref name="rungs"/>, exactly as before AB#5157. Runs once
    /// over the whole ladder and, when the coverage filter excludes something, once more over the
    /// covering candidates.
    /// </summary>
    private static SeriesResolutionResult PlanCore(
        IReadOnlyList<ResolutionRung> rungs,
        DateTime from,
        DateTime to,
        int targetPoints,
        CkRollupFunction requiredAggregation,
        Func<ResolutionRung, DateTime, DateTime, long?> effectiveGrainMs)
    {
        var spanMs = Math.Max(1L, (long)(to - from).TotalMilliseconds);
        var idealBucketMs = Math.Max(1L, spanMs / targetPoints);

        var baseRung = rungs.FirstOrDefault(r => r.IsBase);

        // Eligible reduction sources: rollups whose stored functions include the requested aggregation
        // (a rollup may carry several aggregations on the same path, AB#4188) and whose effective
        // grain is determinate.
        var eligible = rungs
            .Where(r => !r.IsBase && r.StoredFunctionsForSeries.Contains(requiredAggregation))
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

            // The output bucket MUST be an integer multiple of the chosen rung's grain, not the raw
            // pixel-driven ideal. A rollup is a windowed archive: the downsampling engine only folds a
            // stored window into a bin when the whole window is contained in it (concept-time-range §7),
            // so a bin width that is not a multiple of the grain makes every straddling window drop and
            // the series undercounts badly (measured ~6% of truth for a month over the hourly rollup —
            // AB#4714). Snap to the nearest grain multiple: merge = round(ideal / grain) grain windows
            // per bin. merge == 1 (the common case, e.g. a month over the hourly rollup) means "read the
            // rollup at its native grain" — lossless. The frontend aligns the query window to this same
            // grid (line-chart-widget), and the server's DownsamplingBinQuantizer independently arrives
            // at the same whole-window merge, so SQL grid and axis agree.
            var merge = Math.Max(1L,
                (long)Math.Round((double)idealBucketMs / chosen.Grain, MidpointRounding.AwayFromZero));
            var bucketMs = merge * chosen.Grain;
            var points = (int)Math.Max(1L, spanMs / bucketMs);
            return new SeriesResolutionResult(
                chosen.Rung.ArchiveRtId, bucketMs, points, requiredAggregation,
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
