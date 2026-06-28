using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Persists the per-archive recompute state (AB#4184) that lives on the archive entity itself: the
/// dirty-window ledger (Information A), the pending recompute-range work list (Information B), and
/// the observability fields (in-progress flag, last success / failure). Backed by the archive
/// entity's runtime-state attributes so a blueprint re-apply never resets it.
/// </summary>
/// <remarks>
/// Operations target raw, time-range, and rollup archives alike (a rollup is itself a source for
/// chained rollups). Implementations load the archive entity, mutate the relevant
/// runtime-state attribute(s), and persist — mirroring how the watermark / freeze attributes are
/// maintained today.
/// </remarks>
public interface IArchiveRecomputeStateStore
{
    /// <summary>
    /// Appends a retroactive-change record (Information A) to the archive's dirty-window ledger.
    /// Append-style changes that do not make dependents stale need not be recorded.
    /// </summary>
    Task AppendDirtyWindowAsync(OctoObjectId archiveRtId, ArchiveDirtyWindow window);

    /// <summary>Reads the archive's current dirty-window ledger; empty when clean.</summary>
    Task<IReadOnlyList<ArchiveDirtyWindow>> GetDirtyWindowsAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Clears the dirty-window ledger after the orchestrator has translated it into pending
    /// recompute ranges on the dependents.
    /// </summary>
    Task ClearDirtyWindowsAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Appends pending recompute obligations (Information B) to the archive's work list, coalescing
    /// is the caller's concern (see <c>RecomputePlanner.MergeIntervals</c>).
    /// </summary>
    Task EnqueueRecomputeRangesAsync(OctoObjectId archiveRtId, IReadOnlyList<ArchiveRecomputeRange> ranges);

    /// <summary>Reads the archive's pending recompute-range work list; empty when nothing is due.</summary>
    Task<IReadOnlyList<ArchiveRecomputeRange>> GetPendingRecomputeRangesAsync(OctoObjectId archiveRtId);

    /// <summary>Clears the pending recompute-range work list after the ranges have been processed.</summary>
    Task ClearPendingRecomputeRangesAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Sets <c>RecomputeInProgress = true</c> and stamps <c>LastRecomputeStartedAt</c>. Called when a
    /// recompute job for the archive starts.
    /// </summary>
    Task MarkRecomputeStartedAsync(OctoObjectId archiveRtId, DateTime startedAt);

    /// <summary>
    /// Clears <c>RecomputeInProgress</c> and stamps <c>LastRecomputeSuccessAt</c> after a successful
    /// commit.
    /// </summary>
    Task MarkRecomputeSucceededAsync(OctoObjectId archiveRtId, DateTime succeededAt);

    /// <summary>
    /// Clears <c>RecomputeInProgress</c>, stamps <c>LastRecomputeFailureAt</c> and records
    /// <c>LastRecomputeFailureReason</c> so a failed run is debuggable.
    /// </summary>
    Task MarkRecomputeFailedAsync(OctoObjectId archiveRtId, DateTime failedAt, string reason);
}
