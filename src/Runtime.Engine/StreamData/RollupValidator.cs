using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// One declared source of a rollup together with the snapshots the activation-time rules need
/// (AB#5157): the archive-level view of the source (status, target type, captured columns, native
/// window length) and — when the source is itself a rollup — its rollup view, whose
/// <see cref="RollupArchiveSnapshot.BucketAlignment"/> drives the calendar granularity rule.
/// </summary>
/// <param name="Reference">The source reference as declared on the rollup being validated.</param>
/// <param name="Archive">
/// The source archive's current snapshot, loaded via <see cref="IArchiveRuntimeStore.GetAsync"/>
/// (Mongo polymorphism returns the same shape for raw, time-range and rollup sources). <c>null</c>
/// when the source archive does not exist or has been soft-deleted, surfaced via
/// <see cref="RollupSourceMissingException"/>.
/// </param>
/// <param name="Rollup">
/// The source's rollup snapshot when the source is a rollup (loaded via
/// <see cref="IRollupArchiveRuntimeStore.GetAsync"/>), otherwise <c>null</c>. Callers should
/// supply it for rollup sources: without it a calendar-aligned rollup source degrades to a
/// fixed-size source whose window length is its informational <c>BucketSize</c>.
/// </param>
public sealed record RollupActivationSource(
    RollupSourceReference Reference,
    ArchiveSnapshot? Archive,
    RollupArchiveSnapshot? Rollup = null);

/// <summary>
/// Pure-function validators for the rollup-archive save-time and activation-time rules
/// (rollup-archives concept §10, multi-source rules in <c>concept-multi-source-rollups.md</c> §4).
/// No I/O — callers fetch the snapshots and pass them in.
/// </summary>
/// <remarks>
/// <para>
/// The save-time checks — <see cref="ValidateForSave"/> (aggregation rules) and
/// <see cref="ValidateSourcesForSave"/> (source-span rules, AB#5157) — are intentionally cheap so
/// they can run on every create without extra repository hits; <see cref="ValidateNoTransitiveCycle"/>
/// needs the tenant's rollups and runs at create and at activation. The activation-time entry point
/// <see cref="ValidateForActivation"/> re-runs both save-time validators plus the cross-archive rules
/// that need every source's snapshot, so the lifecycle service has a single entry point per phase.
/// </para>
/// <para>
/// Every rule is evaluated per source and throws a distinct <see cref="StreamDataException"/>
/// subclass whose message names the offending source archive, in this order: declaration conflict,
/// no sources, duplicate source, inverted span, second open start / open end, overlapping spans,
/// boundary off the bucket grid (save-time); then per source: missing, not activated, target type
/// mismatch, bucket granularity, aggregation spec unresolvable on the source (activation-time,
/// see <see cref="RollupSourceColumnResolver"/>).
/// </para>
/// </remarks>
public static class RollupValidator
{
    /// <summary>
    /// Validates the structural invariants of a rollup snapshot that do not require any source
    /// archive — non-empty aggregations, no direct self-cycle, no duplicate
    /// <c>(SourcePath, Function)</c> pairs, a <c>ComparisonValue</c> on every StateDuration.
    /// Since AB#5157 this runs at create time as well as at activation.
    /// </summary>
    public static void ValidateForSave(RollupArchiveSnapshot rollup)
    {
        if (rollup.Aggregations is null || rollup.Aggregations.Count == 0)
        {
            throw new RollupAggregationsRequiredException(rollup.RtId);
        }

        if (rollup.Sources is not null && rollup.HasSource(rollup.RtId))
        {
            // Direct cycle. Transitive cycles (A → B → A) need a graph walk over every source edge —
            // see ValidateNoTransitiveCycle, which runs at create and at activation.
            throw new RollupCycleException(rollup.RtId);
        }

        var seen = new HashSet<(string Path, CkRollupFunction Function)>();
        foreach (var agg in rollup.Aggregations)
        {
            if (!seen.Add((agg.SourcePath, agg.Function)))
            {
                throw new DuplicateRollupAggregationException(rollup.RtId, agg.SourcePath, agg.Function);
            }

            // AB#4336: a StateDuration without a state literal has nothing to measure.
            if (agg.Function == CkRollupFunction.StateDuration && string.IsNullOrWhiteSpace(agg.ComparisonValue))
            {
                throw new RollupComparisonValueRequiredException(rollup.RtId, agg.SourcePath);
            }
        }
    }

    /// <summary>
    /// Validates the rollup's source declaration (AB#5157) without loading any source: the
    /// deprecated scalar must not disagree with <see cref="RollupArchiveSnapshot.Sources"/>, at
    /// least one source is required, each archive appears at most once, every bounded span is
    /// non-empty, at most one source has an open start and at most one an open end, the spans are
    /// pairwise disjoint (abutting spans are disjoint because the intervals are half-open), and
    /// every span boundary lies on a bucket boundary of the rollup in its reference time zone
    /// (<see cref="BucketAlignment.FixedSize"/> ⇒ the grid of <c>BucketSize</c> multiples anchored
    /// at <see cref="DateTime"/> tick zero, 0001-01-01T00:00:00Z — identical to a Unix-epoch grid
    /// for any bucket size that divides 24 h, but not for multi-day fixed buckets).
    /// </summary>
    /// <param name="rollup">The rollup snapshot being created or activated.</param>
    public static void ValidateSourcesForSave(RollupArchiveSnapshot rollup)
    {
        var sources = rollup.Sources ?? Array.Empty<RollupSourceReference>();

        if (rollup.ConflictingSourceArchiveRtId is { } conflicting && sources.Count > 0)
        {
            throw new RollupSourceDeclarationConflictException(rollup.RtId, conflicting, sources[0].SourceArchiveRtId);
        }

        if (sources.Count == 0)
        {
            throw new RollupSourcesRequiredException(rollup.RtId);
        }

        var seenArchives = new HashSet<OctoObjectId>();
        foreach (var source in sources)
        {
            if (!seenArchives.Add(source.SourceArchiveRtId))
            {
                throw new DuplicateRollupSourceException(rollup.RtId, source.SourceArchiveRtId);
            }
        }

        foreach (var source in sources)
        {
            if (source.ValidFrom is { } from && source.ValidTo is { } to && from >= to)
            {
                throw new RollupSourceSpanInvertedException(rollup.RtId, source.SourceArchiveRtId, from, to);
            }
        }

        RollupSourceReference? openStart = null;
        RollupSourceReference? openEnd = null;
        foreach (var source in sources)
        {
            if (source.ValidFrom is null)
            {
                if (openStart is not null)
                {
                    throw new RollupSourceSpanOpenEndConflictException(
                        rollup.RtId, source.SourceArchiveRtId, openStart.SourceArchiveRtId, isOpenStart: true);
                }

                openStart = source;
            }

            if (source.ValidTo is null)
            {
                if (openEnd is not null)
                {
                    throw new RollupSourceSpanOpenEndConflictException(
                        rollup.RtId, source.SourceArchiveRtId, openEnd.SourceArchiveRtId, isOpenStart: false);
                }

                openEnd = source;
            }
        }

        // Pairwise disjointness: sorted by ValidFrom (open start first) it suffices to compare each
        // span with its successor — if a span does not reach into the next one it cannot reach into
        // any later one either. An open end reaches into every later span.
        var ordered = sources
            .OrderBy(s => s.ValidFrom ?? DateTime.MinValue)
            .ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var earlier = ordered[i - 1];
            var later = ordered[i];
            var laterStart = later.ValidFrom ?? DateTime.MinValue;
            if (earlier.ValidTo is not { } earlierEnd || earlierEnd > laterStart)
            {
                throw new RollupSourceSpanOverlapException(rollup.RtId, later.SourceArchiveRtId, earlier.SourceArchiveRtId);
            }
        }

        var zone = BucketBoundary.ResolveZone(rollup.ReferenceTimeZone);
        foreach (var source in sources)
        {
            if (source.ValidFrom is { } validFrom && !IsOnBucketBoundary(validFrom, rollup, zone))
            {
                throw new RollupSourceSpanNotOnBucketBoundaryException(
                    rollup.RtId, source.SourceArchiveRtId, nameof(RollupSourceReference.ValidFrom), validFrom);
            }

            if (source.ValidTo is { } validTo && !IsOnBucketBoundary(validTo, rollup, zone))
            {
                throw new RollupSourceSpanNotOnBucketBoundaryException(
                    rollup.RtId, source.SourceArchiveRtId, nameof(RollupSourceReference.ValidTo), validTo);
            }
        }
    }

    /// <summary>
    /// Rejects a source declaration that closes a cycle in the rollup graph (AB#5157): following
    /// source edges from <paramref name="rollup"/> through the tenant's rollups must never reach
    /// <paramref name="rollup"/> again. Iterative depth-first walk over every source edge with a
    /// visited set, so diamonds are visited once and an already-broken graph terminates.
    /// </summary>
    /// <param name="rollup">
    /// The rollup being created or activated. Need not be contained in
    /// <paramref name="rollupsByRtId"/> (at create time it does not exist yet).
    /// </param>
    /// <param name="rollupsByRtId">
    /// Every non-soft-deleted rollup of the tenant keyed by rtId — the graph's edges. Archives
    /// that are not rollups (raw / time-range) have no outgoing edges and need not be present.
    /// </param>
    /// <exception cref="RollupSourceCycleException">
    /// Names the direct source of <paramref name="rollup"/> through which the cycle closes.
    /// </exception>
    public static void ValidateNoTransitiveCycle(
        RollupArchiveSnapshot rollup,
        IReadOnlyDictionary<OctoObjectId, RollupArchiveSnapshot> rollupsByRtId)
    {
        if (rollupsByRtId is null) throw new ArgumentNullException(nameof(rollupsByRtId));

        var visited = new HashSet<OctoObjectId>();
        var stack = new Stack<OctoObjectId>();

        foreach (var directSource in rollup.Sources ?? Array.Empty<RollupSourceReference>())
        {
            stack.Push(directSource.SourceArchiveRtId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == rollup.RtId)
                {
                    throw new RollupSourceCycleException(rollup.RtId, directSource.SourceArchiveRtId);
                }

                // Reachability from a node does not depend on the path taken to it, so a node that
                // was fully explored under an earlier direct source cannot close a cycle now.
                if (!visited.Add(current) || !rollupsByRtId.TryGetValue(current, out var node))
                {
                    continue;
                }

                foreach (var edge in node.Sources ?? Array.Empty<RollupSourceReference>())
                {
                    stack.Push(edge.SourceArchiveRtId);
                }
            }
        }
    }

    /// <summary>
    /// Validates everything required to provision the rollup's CrateDB table and start the
    /// orchestrator: the save-time invariants from <see cref="ValidateForSave"/> and
    /// <see cref="ValidateSourcesForSave"/>, plus — for <em>every</em> source — that it exists, is
    /// activated, targets the same CK type as the rollup, is no finer than the rollup's bucket
    /// (AB#4289 as reformulated by AB#5157) and can serve every aggregation spec of the rollup
    /// (strict: a spec that <see cref="RollupSourceColumnResolver.TryResolve"/> cannot resolve on
    /// any one source is rejected). The transitive cycle rule needs the tenant's rollups and is
    /// run separately via <see cref="ValidateNoTransitiveCycle"/>.
    /// </summary>
    /// <param name="rollup">The rollup snapshot being activated.</param>
    /// <param name="sources">
    /// One entry per <see cref="RollupArchiveSnapshot.Sources"/> reference, in declaration order,
    /// carrying the snapshots loaded for that source (see <see cref="RollupActivationSource"/>).
    /// </param>
    /// <remarks>
    /// Bucket granularity per source (decision 1 of AB#5157): a <see cref="BucketAlignment.FixedSize"/>
    /// rollup keeps the AB#4289 rule — its bucket must be at least, and an integer multiple of, the
    /// source's native window length. A calendar-aligned rollup over a calendar-aligned rollup
    /// source compares alignment coarseness (CalendarYear &gt; CalendarQuarter &gt; CalendarMonth
    /// &gt; CalendarDay; an ISO-8601 week nests only over CalendarDay or fixed-size sources and
    /// nothing nests over a week except a week). A calendar-aligned rollup over a time-range or
    /// fixed-size source accepts the source when its window length does not exceed the alignment's
    /// bucket length (1 d day, 7 d week, 31 d month, 92 d quarter, 366 d year). Raw sources with an
    /// undeclared sampling interval are not checked.
    /// </remarks>
    public static void ValidateForActivation(
        RollupArchiveSnapshot rollup,
        IReadOnlyList<RollupActivationSource> sources)
    {
        if (sources is null) throw new ArgumentNullException(nameof(sources));

        ValidateForSave(rollup);
        ValidateSourcesForSave(rollup);

        foreach (var source in sources)
        {
            var sourceRtId = source.Reference.SourceArchiveRtId;

            if (source.Archive is not { } archive)
            {
                throw new RollupSourceMissingException(rollup.RtId, sourceRtId);
            }

            if (archive.Status != CkArchiveStatus.Activated)
            {
                throw new RollupSourceNotActivatedException(rollup.RtId, sourceRtId, archive.Status);
            }

            if (archive.TargetCkTypeId != rollup.TargetCkTypeId)
            {
                throw new RollupSourceTargetTypeMismatchException(
                    rollup.RtId, sourceRtId, rollup.TargetCkTypeId, archive.TargetCkTypeId);
            }

            ValidateBucketGranularity(rollup, source, archive);

            // Every logical aggregation spec must resolve on every source (AB#5157 decision 2) — a
            // source that cannot serve one would silently produce empty columns for the buckets it
            // serves. Two-step per source (RollupSourceColumnResolver): a captured column addressed
            // verbatim (ingested Path / computed Name / a rollup's physical column name), else — for a
            // rollup source — a child aggregation with the same function and the same normalised
            // source path, so one logical spec ('Amount.Value', Sum) is accepted over a time-range
            // base archive and over an hourly rollup of it alike (AC1 mixed sources).
            foreach (var agg in rollup.Aggregations)
            {
                if (RollupSourceColumnResolver.TryResolve(agg, archive, source.Rollup) is null)
                {
                    throw new RollupSourcePathMissingException(rollup.RtId, sourceRtId, agg.SourcePath);
                }
            }
        }
    }

    private static bool IsOnBucketBoundary(DateTime boundary, RollupArchiveSnapshot rollup, TimeZoneInfo? zone)
    {
        var aligned = BucketBoundary.AlignDown(boundary, rollup.BucketAlignment, rollup.BucketSize, zone);
        return aligned == AsUtc(boundary);
    }

    /// <summary>
    /// Same Kind normalisation as <see cref="BucketBoundary"/> applies before aligning: Local is
    /// converted, Unspecified is taken as UTC — so the aligned value is comparable tick-for-tick.
    /// </summary>
    private static DateTime AsUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };

    /// <summary>
    /// AB#4289 reformulated for calendar-aligned rollups (AB#5157 decision 1). The source
    /// granularity is its window length (TimeRangeArchive <c>Period</c> / rollup <c>BucketSize</c>,
    /// both surfaced via <see cref="ArchiveSnapshot.Period"/>); it is null for raw archives whose
    /// sampling interval is undeclared — those cannot be validated and are skipped rather than
    /// rejected on a guess. A finer or misaligned target bucket upsamples: a raw source yields a
    /// sparse table, a windowed source an empty one.
    /// </summary>
    private static void ValidateBucketGranularity(
        RollupArchiveSnapshot rollup, RollupActivationSource source, ArchiveSnapshot archive)
    {
        var sourceRtId = source.Reference.SourceArchiveRtId;
        var sourceAlignment = source.Rollup?.BucketAlignment ?? BucketAlignment.FixedSize;
        var sourceGranularity = source.Rollup?.BucketSize ?? archive.Period;

        if (rollup.BucketAlignment == BucketAlignment.FixedSize)
        {
            // The original ms rule: at least the source window, and an integer multiple of it.
            if (sourceGranularity is { } granularity && granularity > TimeSpan.Zero &&
                (rollup.BucketSize < granularity || rollup.BucketSize.Ticks % granularity.Ticks != 0))
            {
                throw new RollupBucketIntervalException(
                    rollup.RtId, sourceRtId, rollup.BucketSize, granularity);
            }

            return;
        }

        if (sourceAlignment != BucketAlignment.FixedSize)
        {
            // Calendar over calendar: the source alignment must nest inside the rollup alignment.
            if (!CalendarAlignmentNests(sourceAlignment, rollup.BucketAlignment))
            {
                throw new RollupBucketIntervalException(
                    rollup.RtId, sourceRtId, rollup.BucketAlignment, sourceAlignment);
            }

            return;
        }

        // Calendar over a time-range / fixed-size / raw source: the source window must fit into the
        // alignment's bucket. The threshold is the alignment's bucket length so a legacy archive
        // declared at the same nominal cadence (e.g. quarterly totals with a 92 d Period under a
        // CalendarQuarter rollup, the sbeg cutover shape) is accepted; anything coarser is rejected.
        if (sourceGranularity is { } period && period > TimeSpan.Zero &&
            period > BucketLengthOf(rollup.BucketAlignment))
        {
            throw new RollupBucketIntervalException(rollup.RtId, sourceRtId, rollup.BucketSize, period);
        }
    }

    /// <summary>
    /// True when buckets of <paramref name="source"/> nest inside buckets of
    /// <paramref name="target"/>: equal alignments pass through; the calendar ladder is
    /// CalendarDay ⊂ CalendarMonth ⊂ CalendarQuarter ⊂ CalendarYear; an ISO-8601 week contains
    /// only calendar days and is contained by nothing but itself.
    /// </summary>
    private static bool CalendarAlignmentNests(BucketAlignment source, BucketAlignment target)
    {
        if (source == target)
        {
            return true;
        }

        if (target == BucketAlignment.Iso8601Week)
        {
            return source == BucketAlignment.CalendarDay;
        }

        if (source == BucketAlignment.Iso8601Week)
        {
            return false;
        }

        return CalendarRank(source) <= CalendarRank(target);
    }

    private static int CalendarRank(BucketAlignment alignment) => alignment switch
    {
        BucketAlignment.CalendarDay => 1,
        BucketAlignment.CalendarMonth => 2,
        BucketAlignment.CalendarQuarter => 3,
        BucketAlignment.CalendarYear => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Not a calendar alignment."),
    };

    /// <summary>
    /// The longest bucket a calendar alignment produces, ignoring the ±1 h a DST transition adds
    /// or removes — the cadence a time-range / fixed-size source window must not exceed.
    /// </summary>
    private static TimeSpan BucketLengthOf(BucketAlignment alignment) => alignment switch
    {
        BucketAlignment.CalendarDay => TimeSpan.FromDays(1),
        BucketAlignment.Iso8601Week => TimeSpan.FromDays(7),
        BucketAlignment.CalendarMonth => TimeSpan.FromDays(31),
        BucketAlignment.CalendarQuarter => TimeSpan.FromDays(92),
        BucketAlignment.CalendarYear => TimeSpan.FromDays(366),
        _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Not a calendar alignment."),
    };
}
