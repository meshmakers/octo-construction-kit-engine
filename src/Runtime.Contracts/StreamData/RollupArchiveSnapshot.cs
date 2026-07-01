using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Read-only snapshot of the parts of a <c>CkRollupArchive</c> entity the orchestrator and the
/// lifecycle service need. Extends the data carried by <see cref="ArchiveSnapshot"/> with rollup-
/// specific fields. Backend-specific stores translate from their concrete representation to this
/// record.
/// </summary>
/// <remarks>
/// Concept §3 / §5. <see cref="LastAggregatedBucketEnd"/> is null before the first orchestrator
/// run; it is advanced exclusively by the orchestrator (per bucket commit) and by the
/// <c>rewindRollupWatermark</c> mutation. <see cref="FrozenUntil"/> is null when the rollup is not
/// frozen; once set it is monotonic.
/// </remarks>
public sealed record RollupArchiveSnapshot(
    OctoObjectId RtId,
    RtCkId<CkTypeId> TargetCkTypeId,
    CkArchiveStatus Status,
    string? RtWellKnownName,
    OctoObjectId SourceArchiveRtId,
    TimeSpan BucketSize,
    TimeSpan WatermarkLag,
    DateTime? LastAggregatedBucketEnd,
    IReadOnlyList<CkRollupAggregationSpec> Aggregations,
    DateTime? FrozenUntil)
{
    /// <summary>
    /// Bucket-boundary alignment. <see cref="BucketAlignment.FixedSize"/> (the default for
    /// entities created before System.StreamData 1.4.0) preserves the legacy
    /// <c>LastAggregatedBucketEnd + BucketSize</c> arithmetic. Calendar / ISO-week variants
    /// derive bucket boundaries from the wall clock so monthly / weekly / yearly rollups become
    /// expressible. Concept-time-range §7.
    /// </summary>
    public BucketAlignment BucketAlignment { get; init; } = BucketAlignment.FixedSize;

    /// <summary>
    /// IANA reference time-zone id (e.g. <c>Europe/Vienna</c>) used to align calendar bucket
    /// boundaries (day / week / month / year) to local wall-clock time so they are DST-correct
    /// across countries. <c>null</c> ⇒ UTC calendar boundaries (the pre-AB#4290 behaviour). Only
    /// meaningful for calendar <see cref="BucketAlignment"/> variants; ignored for
    /// <see cref="BucketAlignment.FixedSize"/>. System.StreamData 1.6.4 / decision O6.
    /// </summary>
    public string? ReferenceTimeZone { get; init; }

    // ---------- Recompute observability (AB#4184, Phase 5 follow-up) ----------
    // Init-only so the positional ctor used by the orchestrator / lifecycle stays unchanged; these
    // are projected from the engine-maintained recompute-state attributes on the Archive base and
    // surfaced read-only through rollupsFor so a studio dashboard can show recompute health without
    // a second round-trip. All default to the steady state (idle, nothing pending) for callers
    // that build the snapshot without recompute context.

    /// <summary>
    /// True while a recompute job for this rollup is running or swapping. Mirrors
    /// <c>Archive.RecomputeInProgress</c>.
    /// </summary>
    public bool RecomputeInProgress { get; init; }

    /// <summary>Start timestamp of the most recent recompute run; null before the first run.</summary>
    public DateTime? LastRecomputeStartedAt { get; init; }

    /// <summary>
    /// Finish timestamp of the most recent successfully committed recompute run; null before the
    /// first success.
    /// </summary>
    public DateTime? LastRecomputeSuccessAt { get; init; }

    /// <summary>Timestamp of the most recent failed recompute run; null if the last run succeeded.</summary>
    public DateTime? LastRecomputeFailureAt { get; init; }

    /// <summary>Human-readable reason for the most recent recompute failure; null if the last run succeeded.</summary>
    public string? LastRecomputeFailureReason { get; init; }

    /// <summary>
    /// Number of dirty windows currently recorded on this archive (Information A — retroactive
    /// changes not yet propagated). 0 in the steady state.
    /// </summary>
    public int DirtyWindowsPending { get; init; }

    /// <summary>
    /// Number of pending recompute ranges currently queued on this archive (Information B — the
    /// recompute work list the orchestrator still has to drain). 0 in the steady state.
    /// </summary>
    public int PendingRecomputeRanges { get; init; }
}

/// <summary>
/// Minimal projection of a <c>CkRollupAggregation</c> record — the source column on the source
/// archive, the aggregation function, and an optional explicit target column name. The CK record
/// carries additional fields in some models; the lifecycle / orchestrator only depends on these.
/// </summary>
/// <param name="SourcePath">
/// Attribute path on the source archive. Must resolve against the source archive's
/// <c>Columns</c> list (not the CK type directly) at activation time. For chained rollups that
/// read from a stored AVG, address the materialised <c>_sum</c>/<c>_count</c> columns by name.
/// </param>
/// <param name="Function">The aggregation function applied to <see cref="SourcePath"/>.</param>
/// <param name="TargetColumnName">
/// Optional explicit storage column name. <c>null</c> defaults to
/// <c>"{sourcePath}_{function}"</c> lower-cased. For <see cref="CkRollupFunction.Avg"/>, two
/// columns are emitted with suffixes <c>_sum</c> and <c>_count</c> derived from this base name.
/// </param>
public sealed record CkRollupAggregationSpec(
    string SourcePath,
    CkRollupFunction Function,
    string? TargetColumnName);
