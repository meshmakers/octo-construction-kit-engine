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
    string? StagingTableName);
