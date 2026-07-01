using System;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Tenant-scoped coordinator for optimistic rollup recompute (AB#4184). Exposed as an interface so
/// host wiring (the per-tenant context, the background service) depends on the contract rather than
/// the engine implementation — mirroring <see cref="IRollupOrchestrator"/>.
/// </summary>
public interface IRecomputeOrchestrator
{
    /// <summary>
    /// One periodic tick: fan out every source's dirty windows onto its dependents, then drain each
    /// activated rollup's pending recompute ranges. Returns the number of recompute runs executed.
    /// </summary>
    Task<int> TickAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Executes (or coalesces) one recompute of a rollup over the bucket-aligned range
    /// <c>[from, to)</c>, optionally scoped to a single <paramref name="rtIdScope"/>. Backs the
    /// manual recompute API.
    /// </summary>
    Task<RecomputeJobSnapshot> RecomputeArchiveAsync(
        OctoObjectId rollupRtId,
        DateTime from,
        DateTime to,
        OctoObjectId? rtIdScope,
        RecomputeTrigger trigger,
        CancellationToken cancellationToken);

    /// <summary>
    /// Turns a source archive's retroactive dirty windows into pending recompute ranges on its
    /// direct dependents, then clears the windows (Information A → B).
    /// </summary>
    Task PropagateDirtyWindowsAsync(OctoObjectId sourceArchiveRtId, CancellationToken cancellationToken);

    /// <summary>
    /// Queues a <em>durable, background</em> backfill of a rollup over the <em>entire</em> history of
    /// its source archive without the operator supplying a timestamp (AB#4269 / AB#4286). Resolves the
    /// source archive's earliest stored timestamp, snaps it down to the rollup's bucket boundary, and
    /// <b>enqueues a persisted pending recompute range</b> <c>[sourceMin, now)</c> plus a
    /// <see cref="RecomputeJobState.Pending"/> <see cref="RecomputeJobSnapshot"/>, then returns
    /// <em>immediately</em> — it does <b>not</b> run the recompute inline.
    /// <para>
    /// The heavy recompute is executed later by the background recompute orchestrator tick
    /// (<see cref="TickAsync"/>), which adopts the pre-created Pending job and drives it
    /// Pending → Running → Completed under the host's application-lifetime cancellation token, never a
    /// caller's HTTP request token. This decouples a multi-minute (e.g. decade-long) backfill from the
    /// client request lifetime so a client HTTP timeout / disconnect can no longer cancel it, and the
    /// persisted pending range survives an asset-repo restart (picked up on the next tick).
    /// </para>
    /// <para>
    /// The returned Pending job is pollable via the recompute-jobs query so the caller can watch its
    /// own backfill progress. When a recompute job is already active for the rollup, the range is
    /// folded into it and that active job is returned instead. Returns <c>null</c> when the source
    /// archive holds no data (no-op).
    /// </para>
    /// </summary>
    Task<RecomputeJobSnapshot?> EnqueueBackfillFromSourceAsync(
        OctoObjectId rollupRtId, CancellationToken cancellationToken);
}
