using System;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Tenant-scoped orchestrator that drives <c>CkRollupArchive</c> aggregation. On each tick the
/// orchestrator enumerates activated, non-frozen rollups via <see cref="IRollupArchiveRuntimeStore"/>
/// and processes due buckets: for each bucket it issues an
/// <c>INSERT INTO target (...) SELECT ... FROM source WHERE timestamp ∈ bucket GROUP BY rtId</c>
/// against the stream-data store, then advances the watermark. DB-neutral: SQL emission lives in
/// the data-store provider (<see cref="IStreamDataRepository"/>).
/// </summary>
/// <remarks>
/// Concept §5. Operation order per bucket: <em>upsert rows first, advance watermark last</em>.
/// On crash between the two, the next tick re-aggregates the same bucket; the upsert collapses
/// duplicates via the natural key <c>(timestamp, rtId)</c>. After <c>maxFailures</c> consecutive
/// failures on the same rollup, status is flipped to <see cref="CkArchiveStatus.Failed"/> and an
/// alert is recorded via <see cref="IArchiveAuditTrail"/> (§8).
/// </remarks>
public interface IRollupOrchestrator
{
    /// <summary>
    /// Runs one orchestrator tick: enumerates all activated, non-frozen rollups for the tenant and
    /// processes any buckets that are due (i.e. <c>bucketEnd ≤ now - WatermarkLag</c>). Returns
    /// the number of buckets that were successfully committed in this tick. Safe to call
    /// concurrently with itself only when the underlying watermark store enforces single-writer
    /// semantics per rollup; in practice the background host invokes it sequentially per tenant.
    /// </summary>
    Task<int> TickAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Processes due buckets for a single rollup. Exposed for targeted runs (e.g. after a manual
    /// <c>rewindRollupWatermark</c>) and for tests. Returns the number of buckets committed.
    /// </summary>
    Task<int> ProcessRollupAsync(OctoObjectId rollupRtId, CancellationToken cancellationToken);

    /// <summary>
    /// Resets the rollup's watermark to <paramref name="toBucketEnd"/> (truncated down to the
    /// bucket boundary). Destructive: rows in the rewound range will be re-aggregated and may
    /// temporarily disagree with previously committed values until the orchestrator catches up.
    /// Backs the <c>rewindRollupWatermark</c> GraphQL mutation.
    /// </summary>
    Task RewindWatermarkAsync(OctoObjectId rollupRtId, DateTime toBucketEnd);
}
