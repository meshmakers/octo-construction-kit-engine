using System;
using System.Collections.Generic;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Pure planning logic for the recompute model (AB#4184): turns a retroactive change on a source
/// archive (<see cref="ArchiveDirtyWindow"/>, Information A) into the per-dependent, bucket-aligned
/// recompute obligations (<see cref="ArchiveRecomputeRange"/>, Information B), and coalesces
/// overlapping ranges. No I/O, no clock — the caller supplies the dependents (via
/// <see cref="IRollupDependencyGraph"/>) and the enqueue timestamp, so every method here is
/// deterministic and unit-testable.
/// </summary>
public static class RecomputePlanner
{
    /// <summary>
    /// Upper bound on bucket-stepping iterations in <see cref="PlanChunks"/>, mirroring the executor's
    /// bucket enumerator. A 10-year hourly range is ~87k buckets, far below this — the guard only trips
    /// on a pathological (mis-configured sub-second) bucket size.
    /// </summary>
    private const int RunawayGuard = 10_000_000;

    /// <summary>
    /// Builds one <see cref="ArchiveRecomputeRange"/> per dependent for the given dirty window, each
    /// snapped to that dependent's own bucket boundaries. Returns an empty list when the change was
    /// a forward <see cref="RecomputeChangeKind.Append"/> (which the watermark orchestrator already
    /// covers) — only <see cref="RecomputeChangeKind.RetroactiveModify"/> makes dependents stale.
    /// </summary>
    public static IReadOnlyList<ArchiveRecomputeRange> BuildRecomputeRanges(
        ArchiveDirtyWindow window,
        IReadOnlyList<RollupArchiveSnapshot> dependents,
        DateTime enqueuedAt)
    {
        if (window.ChangeKind != RecomputeChangeKind.RetroactiveModify || dependents.Count == 0)
        {
            return Array.Empty<ArchiveRecomputeRange>();
        }

        var ranges = new List<ArchiveRecomputeRange>(dependents.Count);
        foreach (var dependent in dependents)
        {
            var (start, end) = AlignRangeToBuckets(
                window.WindowStart, window.WindowEnd, dependent.BucketAlignment, dependent.BucketSize,
                BucketBoundary.ResolveZone(dependent.ReferenceTimeZone));

            ranges.Add(new ArchiveRecomputeRange(
                dependent.RtId, start, end, RtIdScope: null, enqueuedAt));
        }

        return ranges;
    }

    /// <summary>
    /// Snaps a changed window <c>[windowStart, windowEnd)</c> outward to the dependent's bucket
    /// boundaries, returning the half-open range of whole buckets that overlap the change. A window
    /// that ends exactly on a boundary does not drag in the next bucket; an empty or sub-bucket
    /// window still yields at least one bucket so the change is never silently dropped.
    /// </summary>
    public static (DateTime Start, DateTime End) AlignRangeToBuckets(
        DateTime windowStart, DateTime windowEnd, BucketAlignment alignment, TimeSpan bucketSize,
        TimeZoneInfo? zone = null)
    {
        var start = BucketBoundary.AlignDown(windowStart, alignment, bucketSize, zone);
        var alignedEnd = BucketBoundary.AlignDown(windowEnd, alignment, bucketSize, zone);
        var end = alignedEnd >= windowEnd
            ? alignedEnd
            : BucketBoundary.NextBucketEnd(alignedEnd, alignment, bucketSize, zone);

        if (end <= start)
        {
            end = BucketBoundary.NextBucketEnd(start, alignment, bucketSize, zone);
        }

        return (start, end);
    }

    /// <summary>
    /// Splits a bucket-aligned recompute range <c>[from, to)</c> into contiguous, non-overlapping
    /// sub-ranges of at most <paramref name="maxBucketsPerChunk"/> whole buckets each (AB#4283). This
    /// is the chunk planner that keeps every executor sub-run's staging→swap statements well under the
    /// CrateDB per-statement / Polly timeout: a 10-year hourly recompute (~87k buckets) processed in
    /// one shot exhausts the 30s statement timeout on the staging→live copy and sweep, so the
    /// orchestrator drives the executor once per chunk instead. Chunk boundaries always fall on bucket
    /// boundaries (walked via the same arithmetic the executor's bucket enumerator uses), so no bucket
    /// is ever split across two chunks and none is processed twice.
    /// </summary>
    /// <remarks>
    /// Returns a single chunk when the whole range fits in <paramref name="maxBucketsPerChunk"/>
    /// buckets (the common small-range case — identical to the pre-chunking behaviour), and an empty
    /// list for an empty or inverted range. The final chunk mirrors the executor's bucket enumerator:
    /// if <paramref name="to"/> is not bucket-aligned the last bucket extends past it, exactly as a
    /// single un-chunked call would.
    /// </remarks>
    public static IReadOnlyList<(DateTime Start, DateTime End)> PlanChunks(
        DateTime from, DateTime to, BucketAlignment alignment, TimeSpan bucketSize, int maxBucketsPerChunk,
        TimeZoneInfo? zone = null)
    {
        if (maxBucketsPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBucketsPerChunk), maxBucketsPerChunk, "Chunk size must be positive.");
        }

        var chunks = new List<(DateTime Start, DateTime End)>();
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to);
        if (toUtc <= fromUtc)
        {
            return chunks;
        }

        var chunkStart = fromUtc;
        var cursor = fromUtc;
        var bucketsInChunk = 0;
        var guard = 0;
        while (cursor < toUtc)
        {
            var next = BucketBoundary.NextBucketEnd(cursor, alignment, bucketSize, zone);
            if (next <= cursor)
            {
                break; // defensive: zero / negative bucket would loop forever
            }

            cursor = next;
            bucketsInChunk++;

            if (bucketsInChunk >= maxBucketsPerChunk)
            {
                chunks.Add((chunkStart, cursor));
                chunkStart = cursor;
                bucketsInChunk = 0;
            }

            if (++guard > RunawayGuard)
            {
                break;
            }
        }

        if (bucketsInChunk > 0)
        {
            chunks.Add((chunkStart, cursor));
        }

        return chunks;
    }

    /// <summary>
    /// Coalesces overlapping or touching <c>[Start, End)</c> intervals into the minimal set of
    /// disjoint intervals, ordered by start. Used by the recompute orchestrator's coalesce policy to
    /// fold a freshly triggered range into the ranges already pending for the same archive.
    /// </summary>
    public static IReadOnlyList<(DateTime Start, DateTime End)> MergeIntervals(
        IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var sorted = new List<(DateTime Start, DateTime End)>(intervals);
        sorted.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var interval in sorted)
        {
            if (interval.End <= interval.Start)
            {
                // Degenerate / empty interval — nothing to recompute.
                continue;
            }

            if (merged.Count == 0)
            {
                merged.Add(interval);
                continue;
            }

            var last = merged[merged.Count - 1];
            if (interval.Start <= last.End)
            {
                // Overlapping or adjacent — extend the running interval.
                merged[merged.Count - 1] = (last.Start, interval.End > last.End ? interval.End : last.End);
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
