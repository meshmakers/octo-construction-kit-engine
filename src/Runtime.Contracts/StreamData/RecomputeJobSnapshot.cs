using System;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Read/write snapshot of a <c>RecomputeJob</c> entity (AB#4184) — the persistent history record of
/// one recompute run, carrying enough context to debug a failed recompute after the fact. Stores
/// translate between this record and their concrete entity representation. All timestamps are UTC.
/// </summary>
/// <param name="RtId">
/// Runtime id of the job entity. <see cref="OctoObjectId.Empty"/> for a not-yet-persisted job that
/// <see cref="IRecomputeJobStore.CreateAsync"/> is about to assign an id to.
/// </param>
/// <param name="ArchiveRtId">The archive this job recomputed.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Trigger">What initiated the run.</param>
/// <param name="RangeStart">Inclusive start of the recomputed range.</param>
/// <param name="RangeEnd">Exclusive end of the recomputed range.</param>
/// <param name="RtIdScope">Optional single rtId the run was scoped to; <c>null</c> means all rtIds.</param>
/// <param name="RowsProcessed">Rows written into the staging table; <c>null</c> until known.</param>
/// <param name="WindowsProcessed">Buckets recomputed; <c>null</c> until known.</param>
/// <param name="StartedAt">When compute started; <c>null</c> while pending.</param>
/// <param name="FinishedAt">When the job reached a terminal state; <c>null</c> while running.</param>
/// <param name="DurationMs">Wall-clock duration in ms; <c>null</c> while running.</param>
/// <param name="ErrorReason">Failure reason when <see cref="State"/> is Failed; <c>null</c> otherwise.</param>
/// <param name="StagingTableName">Per-job staging table name, for post-mortem; <c>null</c> once swept.</param>
/// <param name="LastProgressAt">
/// Heartbeat (AB#5189): stamped when the job is created or starts computing and again after every
/// committed chunk. A non-terminal job whose heartbeat is older than the orchestrator's stale-job
/// timeout belongs to a process that died; the drain fails it and carries on with its queued work.
/// <c>null</c> on jobs written before the field existed.
/// </param>
public sealed record RecomputeJobSnapshot(
    OctoObjectId RtId,
    OctoObjectId ArchiveRtId,
    RecomputeJobState State,
    RecomputeTrigger Trigger,
    DateTime RangeStart,
    DateTime RangeEnd,
    OctoObjectId? RtIdScope,
    int? RowsProcessed,
    int? WindowsProcessed,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int? DurationMs,
    string? ErrorReason,
    string? StagingTableName,
    DateTime? LastProgressAt = null)
{
    /// <summary>
    /// Prefix of <see cref="StagingTableName"/> that marks a computed-column backfill job (AB#8002).
    /// A computed-column backfill has no staging table; its target is the hidden physical column the
    /// backfill writes before the column is switched on. The marker lets such a job live in the same
    /// <c>RecomputeJob</c> history as the rollup recomputes — so it shows up in the recompute-jobs
    /// query — without a model change, and keeps the rollup orchestrator from adopting it.
    /// </summary>
    public const string ComputedColumnMarkerPrefix = "computed-column:";

    /// <summary>
    /// Follows <see cref="ComputedColumnMarkerPrefix"/> in the marker of a computed-column <b>repair</b> job
    /// (AB#8331): <c>computed-column:repair:&lt;column&gt;</c>. Column names never contain a colon, so the
    /// marker is unambiguous; a repair job counts as a computed-column job everywhere (rollup orchestrator,
    /// recompute-jobs query) and reports its column in <see cref="ComputedColumnName"/>.
    /// </summary>
    public const string ComputedColumnRepairMarker = "repair:";

    /// <summary>
    /// The computed column a backfill or repair job works on, or <c>null</c> for a rollup recompute job.
    /// </summary>
    public string? ComputedColumnName
    {
        get
        {
            if (StagingTableName is not { } marker || !marker.StartsWith(ComputedColumnMarkerPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var name = marker.Substring(ComputedColumnMarkerPrefix.Length);
            return name.StartsWith(ComputedColumnRepairMarker, StringComparison.Ordinal)
                ? name.Substring(ComputedColumnRepairMarker.Length)
                : name;
        }
    }

    /// <summary>Whether this job is a computed-column job (backfill or repair) rather than a rollup recompute.</summary>
    public bool IsComputedColumnBackfill => ComputedColumnName is not null;

    /// <summary>Whether this job repairs NULL cells of an active computed column (AB#8331).</summary>
    public bool IsComputedColumnRepair =>
        StagingTableName is { } marker
        && marker.StartsWith(ComputedColumnMarkerPrefix + ComputedColumnRepairMarker, StringComparison.Ordinal);

    /// <summary>Whether the job is still waiting or running (Pending / Running / Swapping).</summary>
    public bool IsActive => State is RecomputeJobState.Pending or RecomputeJobState.Running or RecomputeJobState.Swapping;

    /// <summary>
    /// A new, not yet persisted computed-column backfill job (AB#8002). The range is
    /// <c>[UnixEpoch, createdAt)</c>: the backfill covers every row the archive held when it was queued.
    /// While the job runs, <see cref="RangeStart"/> advances to the backfill's checkpoint (the end of the
    /// last completed time slice), so a run cut short by a restart resumes there instead of starting over.
    /// </summary>
    public static RecomputeJobSnapshot ForComputedColumn(
        OctoObjectId archiveRtId, string columnName, RecomputeJobState state, RecomputeTrigger trigger,
        DateTime createdAt) =>
        new(OctoObjectId.Empty, archiveRtId, state, trigger, DateTime.UnixEpoch, createdAt, null,
            null, null, state == RecomputeJobState.Running ? createdAt : null, null, null, null,
            ComputedColumnMarkerPrefix + columnName, createdAt);

    /// <summary>
    /// A new, not yet persisted computed-column repair job (AB#8331) over <c>[from, to)</c>. While the job
    /// runs, <see cref="RangeStart"/> advances to the repair's checkpoint like a backfill's.
    /// </summary>
    public static RecomputeJobSnapshot ForComputedColumnRepair(
        OctoObjectId archiveRtId, string columnName, DateTime from, DateTime to, RecomputeTrigger trigger,
        DateTime createdAt) =>
        new(OctoObjectId.Empty, archiveRtId, RecomputeJobState.Pending, trigger, from, to, null,
            null, null, null, null, null, null,
            ComputedColumnMarkerPrefix + ComputedColumnRepairMarker + columnName, createdAt);
}
