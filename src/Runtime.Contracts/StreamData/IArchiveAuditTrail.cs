using System;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Records archive status transitions as audit events. Bridged at deployment time to the platform
/// notification/event repository (see concept §14); kept as a focused interface so the lifecycle
/// service has no direct dependency on the notifications package.
/// </summary>
public interface IArchiveAuditTrail
{
    /// <summary>
    /// Records a status transition. <paramref name="reason"/> carries the underlying error code
    /// or free-text reason for transitions to <see cref="CkArchiveStatus.Failed"/>; <c>null</c>
    /// for routine transitions. <paramref name="tenantId"/> identifies the tenant whose archive
    /// transitioned; consumers (notification bus, audit log) need it for routing and
    /// correlation.
    /// </summary>
    Task RecordTransitionAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        CkArchiveStatus from,
        CkArchiveStatus to,
        string? reason);

    /// <summary>
    /// Records a deletion (any state → soft-deleted entity + dropped Crate table).
    /// </summary>
    Task RecordDeletionAsync(string tenantId, OctoObjectId archiveRtId, CkArchiveStatus statusAtDeletion);

    /// <summary>
    /// Records one committed rollup bucket. Emitted by <see cref="IRollupOrchestrator"/> after a
    /// bucket's rows were upserted and the watermark advanced. Concept (rollup-archives) §11.
    /// </summary>
    Task RecordRollupRunAsync(
        string tenantId,
        OctoObjectId rollupRtId,
        DateTime bucketStart,
        DateTime bucketEnd,
        int rowsWritten,
        TimeSpan elapsed);

    /// <summary>
    /// Records a freeze (manual or implicit when source data was truncated). <paramref name="reason"/>
    /// carries a free-text reason or the triggering event id; <c>null</c> for explicit operator
    /// freezes via the GraphQL mutation. Concept (rollup-archives) §11.
    /// </summary>
    Task RecordFreezeAsync(
        string tenantId,
        OctoObjectId rollupRtId,
        DateTime frozenUntil,
        string? reason);

    /// <summary>
    /// Records a successfully committed recompute of an archive over a bucket-aligned range
    /// (AB#4184). Emitted by the recompute orchestrator after the atomic swap. Concept §7.
    /// </summary>
    Task RecordRecomputeRunAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime rangeStart,
        DateTime rangeEnd,
        int rowsProcessed,
        int windowsProcessed,
        TimeSpan elapsed);

    /// <summary>
    /// Records a failed recompute of an archive over a range (AB#4184). <paramref name="reason"/>
    /// carries the failure message so a failed run is debuggable. The previous committed state is
    /// left intact. Concept §7.
    /// </summary>
    Task RecordRecomputeFailureAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime rangeStart,
        DateTime rangeEnd,
        string reason);

    /// <summary>
    /// Records that a retroactive write to a source archive reached <b>beyond</b> the bounded
    /// retro-reach cap (AB#4196), so the out-of-reach tail was <b>not</b> scheduled for an automatic
    /// recompute. Surfaces the drop so an operator can run an unbounded manual
    /// <c>recomputeArchive</c> for the deeper range if it must be corrected.
    /// <paramref name="consumedWatermark"/> is the frontier the write was measured against;
    /// <paramref name="cappedFloor"/> = <c>consumedWatermark - cap</c> is the earliest point still in
    /// automatic reach; <paramref name="cap"/> is the effective cap
    /// (<c>min(per-archive, fleet ceiling)</c>) that applied.
    /// </summary>
    Task RecordRetroReachCappedAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime consumedWatermark,
        DateTime cappedFloor,
        TimeSpan cap);
}
