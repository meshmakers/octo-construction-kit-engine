using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Performs the actual recompute of a rollup over a bucket-aligned range (AB#4184): aggregate the
/// source archive into a per-job staging table and commit it atomically (full-archive
/// <c>SWAP TABLE</c> or per-window generation-pointer flip), so readers never observe a partial
/// state. The orchestrator owns the job lifecycle, coalescing, observability, and dependency
/// propagation; this interface owns only the storage-level compute + swap. The CrateDB
/// implementation lands in Phase 3c.
/// </summary>
public interface IArchiveRecomputeExecutor
{
    /// <summary>
    /// Recomputes <paramref name="rollup"/> for the half-open range
    /// <c>[rangeStart, rangeEnd)</c> (optionally restricted to <paramref name="rtIdScope"/>) by
    /// aggregating <paramref name="source"/> into staging and swapping atomically on success. Throws
    /// if the compute or swap fails — leaving the previous committed state intact — so the
    /// orchestrator can mark the job <see cref="RecomputeJobState.Failed"/>.
    /// </summary>
    /// <remarks>
    /// Single-source per call, unchanged by AB#5157: <paramref name="source"/> is authoritative for
    /// the whole range passed in. For a multi-source rollup the orchestrator splits the requested
    /// range into per-source segments along the sources' validity spans first and calls in once per
    /// segment, accumulating the job totals — so a segment never mixes two sources.
    /// </remarks>
    Task<RecomputeExecutionResult> ExecuteAsync(
        ArchiveSnapshot source,
        RollupArchiveSnapshot rollup,
        DateTime rangeStart,
        DateTime rangeEnd,
        OctoObjectId? rtIdScope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes the series of <paramref name="rtIds"/> of <paramref name="rollup"/> in place for the half-open range
    /// <c>[rangeStart, rangeEnd)</c> (AB#9016, late-delivery recompute): their buckets are aggregated from
    /// <paramref name="source"/> again and written at the generation each (bucket, rtId) row is read at today, so no new
    /// generation, no pointer entry and no sweep of superseded rows is needed. Every other series is left untouched. The
    /// result equals what <see cref="ExecuteAsync"/> over the whole range produces for these series.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must only run while no other recompute of the rollup runs: the orchestrator's active-job guard serialises it
    /// with every unscoped, per-rtId and set-scoped recompute of the same rollup. A row is replaced atomically, so a reader
    /// sees each series either before or after the refresh.
    /// </para>
    /// <para>
    /// The default implementation exists so that an implementation compiled against the contract before AB#9016 keeps
    /// working in a mixed package set: one rtId is recomputed scoped through <see cref="ExecuteAsync"/>, several are
    /// recomputed with one unscoped <see cref="ExecuteAsync"/> over the range (the behaviour before AB#9016 - correct for
    /// every series, one generation instead of one per rtId). The CrateDB executor overrides it with the in-place refresh.
    /// </para>
    /// </remarks>
    Task<RecomputeExecutionResult> ExecuteScopedRefreshAsync(
        ArchiveSnapshot source,
        RollupArchiveSnapshot rollup,
        DateTime rangeStart,
        DateTime rangeEnd,
        RtIdSet rtIds,
        CancellationToken cancellationToken)
    {
        OctoObjectId? scope = rtIds.Count == 1 ? rtIds.First() : null;
        return ExecuteAsync(source, rollup, rangeStart, rangeEnd, scope, cancellationToken);
    }
}
