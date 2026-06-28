namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Lifecycle state of a single archive recompute run (AB#4184). Engine-managed. Mirrors the
/// <c>CkRecomputeJobState</c> CK enum (System.StreamData ≥ 1.6.0); key values match so the snapshot
/// maps by a direct cast.
/// </summary>
public enum RecomputeJobState
{
    /// <summary>The job is scheduled but compute has not started yet.</summary>
    Pending = 0,

    /// <summary>
    /// Recomputed buckets are being written into the per-job staging table. Readers still see the
    /// previous state.
    /// </summary>
    Running = 1,

    /// <summary>
    /// Staging is complete and the atomic commit (full-archive SWAP TABLE or per-window
    /// generation-pointer flip) is in progress.
    /// </summary>
    Swapping = 2,

    /// <summary>The recompute committed atomically; readers now see the new values.</summary>
    Completed = 3,

    /// <summary>
    /// The job failed before committing. Staging is discarded; the previous archive state is intact
    /// and consumers never saw a partial result.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// The triggering request was merged into an already-active job for the same archive (its range
    /// was folded into that job).
    /// </summary>
    Coalesced = 5
}
