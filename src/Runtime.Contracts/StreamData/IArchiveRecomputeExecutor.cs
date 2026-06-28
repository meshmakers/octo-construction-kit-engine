using System;
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
    Task<RecomputeExecutionResult> ExecuteAsync(
        ArchiveSnapshot source,
        RollupArchiveSnapshot rollup,
        DateTime rangeStart,
        DateTime rangeEnd,
        OctoObjectId? rtIdScope,
        CancellationToken cancellationToken);
}
