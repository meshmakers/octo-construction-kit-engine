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
                window.WindowStart, window.WindowEnd, dependent.BucketAlignment, dependent.BucketSize);

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
        DateTime windowStart, DateTime windowEnd, BucketAlignment alignment, TimeSpan bucketSize)
    {
        var start = BucketBoundary.AlignDown(windowStart, alignment, bucketSize);
        var alignedEnd = BucketBoundary.AlignDown(windowEnd, alignment, bucketSize);
        var end = alignedEnd >= windowEnd
            ? alignedEnd
            : BucketBoundary.NextBucketEnd(alignedEnd, alignment, bucketSize);

        if (end <= start)
        {
            end = BucketBoundary.NextBucketEnd(start, alignment, bucketSize);
        }

        return (start, end);
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
}
