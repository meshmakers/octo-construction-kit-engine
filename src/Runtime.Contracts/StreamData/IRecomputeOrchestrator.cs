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
}
