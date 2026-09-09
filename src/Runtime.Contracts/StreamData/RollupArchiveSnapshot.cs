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
/// <para>
/// Concept §3 / §5. <see cref="LastAggregatedBucketEnd"/> is null before the first orchestrator
/// run; it is advanced exclusively by the orchestrator (per bucket commit) and by the
/// <c>rewindRollupWatermark</c> mutation. <see cref="FrozenUntil"/> is null when the rollup is not
/// frozen; once set it is monotonic.
/// </para>
/// <para>
/// Since System.StreamData 1.8.0 (AB#5157) a rollup declares <em>several</em> time-disjoint
/// sources via <see cref="Sources"/> instead of a single source archive id, so history imported at
/// a coarser granularity and data ingested natively at a finer granularity end up in one
/// continuous rollup ladder. The snapshot is always normalised — the deprecated single-id storage
/// form arrives here as exactly one unbounded <see cref="RollupSourceReference"/> — so every
/// engine consumer works on <see cref="Sources"/> alone and never on the raw entity fields.
/// See <c>concept-multi-source-rollups.md</c>.
/// </para>
/// </remarks>
/// <param name="RtId">Runtime id of the rollup archive entity.</param>
/// <param name="TargetCkTypeId">CK type the rollup rows live on — inherited from its sources.</param>
/// <param name="Status">Current archive lifecycle status.</param>
/// <param name="RtWellKnownName">Optional human-readable name; null falls back to the rtId.</param>
/// <param name="Sources">
/// The normalised source archives this rollup aggregates from, each with an optional validity
/// span. Never empty for a valid rollup — an empty list means the entity declares no source at all
/// and is rejected at activation. Spans are pairwise disjoint and every bucket lies entirely
/// within one source's span or within none (see <see cref="SourceForBucket"/>).
/// </param>
/// <param name="BucketSize">Bucket width.</param>
/// <param name="WatermarkLag">How long the orchestrator waits after bucket-end before aggregating.</param>
/// <param name="LastAggregatedBucketEnd">Exclusive end of the most recently committed bucket; null before the first run.</param>
/// <param name="Aggregations">The rollup's logical aggregation specs (source path + function).</param>
/// <param name="FrozenUntil">When set, no new bucket whose end is at or before this timestamp is produced.</param>
public sealed record RollupArchiveSnapshot(
    OctoObjectId RtId,
    RtCkId<CkTypeId> TargetCkTypeId,
    CkArchiveStatus Status,
    string? RtWellKnownName,
    IReadOnlyList<RollupSourceReference> Sources,
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

    /// <summary>
    /// How far before a bucket's start the <see cref="CkRollupFunction.TimeWeightedAvg"/>
    /// carry-in scan (LOCF opening state) looks for the latest source observation. Bounds the
    /// per-bucket source scan. <c>null</c> ⇒ the engine default of 35 days. Only consulted by
    /// TimeWeightedAvg aggregations; ignored otherwise. System.StreamData 1.6.5 / AB#4336
    /// decision D1.
    /// </summary>
    public TimeSpan? CarryLookback { get; init; }

    /// <summary>
    /// True when the entity carries at least one persisted (ingested, non-computed) column on its
    /// inherited <c>Archive.Columns</c> slot — i.e. the dehydrated
    /// <see cref="RollupColumnGenerator"/> cache exists. <c>false</c> flags the defect state
    /// produced by ImportRt-seeded records, which arrive with <see cref="Aggregations"/> but no
    /// Columns attribute — breaking the non-null <c>columns</c> GraphQL field for the whole
    /// archives list (AB#4771/AB#4772); computed columns alone don't count. When false, callers
    /// holding a write path (orchestrator tick, lifecycle activation) heal via
    /// <see cref="IRollupArchiveRuntimeStore.TryPersistDerivedColumnsAsync"/>.
    /// </summary>
    /// <remarks>
    /// The rollup read path re-derives the columns from <see cref="Aggregations"/> and is therefore
    /// unaffected, but consumers that read the source archive's persisted Columns list — above all
    /// chained-rollup activation, which resolves an aggregation's <c>SourcePath</c> against it —
    /// see nothing. Defaults to <c>true</c> so callers constructing a snapshot without persistence
    /// context (tests, in-memory stores) are not treated as defective.
    /// </remarks>
    public bool HasPersistedColumns { get; init; } = true;

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

    // ---------- Multi-source declaration (AB#5157, System.StreamData 1.8.0) ----------

    /// <summary>
    /// The deprecated <c>SourceArchiveRtId</c> scalar the entity still carries when it disagrees
    /// with <see cref="Sources"/> — i.e. both storage forms are set and <see cref="Sources"/> is
    /// not exactly one unbounded reference to the same archive. <c>null</c> in every other case.
    /// </summary>
    /// <remarks>
    /// Set exclusively by the single normalisation point on the read side (the runtime store's
    /// snapshot mapping); <see cref="Sources"/> is always kept as the authoritative declaration so
    /// enumeration never throws. Activation rejects a snapshot that carries this flag
    /// (<see cref="RollupSourceDeclarationConflictException"/>) instead of silently picking one of
    /// the two declarations.
    /// </remarks>
    public OctoObjectId? ConflictingSourceArchiveRtId { get; init; }

    /// <summary>
    /// The single source archive id for read surfaces that still expose the deprecated
    /// <c>sourceArchiveRtId</c> field — non-null only when the rollup declares exactly one source
    /// and that source is unbounded. <c>null</c> for every genuinely multi-source rollup, for a
    /// single source carrying a validity span, and for a rollup without sources.
    /// </summary>
    public OctoObjectId? SingleUnboundedSourceRtId =>
        Sources is [{ IsUnbounded: true } single] ? single.SourceArchiveRtId : null;

    /// <summary>
    /// Picks the source that is authoritative for the half-open bucket
    /// <c>[<paramref name="bucketStart"/>, <paramref name="bucketEnd"/>)</c> — the first reference
    /// whose validity span contains the bucket <em>entirely</em>. Returns <c>null</c> when no span
    /// covers the bucket; the orchestrator then writes no row for it and advances the watermark.
    /// </summary>
    /// <param name="bucketStart">Inclusive start of the bucket.</param>
    /// <param name="bucketEnd">Exclusive end of the bucket.</param>
    /// <remarks>
    /// Because the spans are validated as pairwise disjoint and aligned to the rollup's bucket
    /// grid, at most one source can ever contain a given bucket — the first match is the only
    /// match.
    /// </remarks>
    public RollupSourceReference? SourceForBucket(DateTime bucketStart, DateTime bucketEnd)
    {
        foreach (var source in Sources)
        {
            if (source.Contains(bucketStart, bucketEnd))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="archiveRtId"/> is one of this rollup's declared sources,
    /// regardless of the validity span it carries. The membership test behind the dependency
    /// graph, the source delete guard and <c>rollupsFor</c>.
    /// </summary>
    /// <param name="archiveRtId">Runtime id of the candidate source archive.</param>
    public bool HasSource(OctoObjectId archiveRtId)
    {
        foreach (var source in Sources)
        {
            if (source.SourceArchiveRtId == archiveRtId)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// One source archive of a <see cref="RollupArchiveSnapshot"/> together with the half-open validity
/// span in which it is authoritative (AB#5157, System.StreamData 1.8.0).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ValidFrom"/> is inclusive and <see cref="ValidTo"/> is exclusive, so two references
/// that abut at the same timestamp are disjoint: the typical cutover setup is one legacy source
/// with <c>ValidTo = cutover</c> and one native source with <c>ValidFrom = cutover</c>, and the
/// cutover bucket belongs to the native source. An unset bound means "open" in that direction.
/// </para>
/// <para>
/// The validator enforces that spans are pairwise disjoint, that at most one reference has an open
/// start and at most one an open end, and that every boundary lies on a bucket boundary of the
/// rollup in its reference time zone — so a bucket is always contained in exactly one span or in
/// none. See <c>concept-multi-source-rollups.md</c>.
/// </para>
/// </remarks>
/// <param name="SourceArchiveRtId">
/// Runtime id of the source archive (raw, time-range, or another rollup for a chained ladder).
/// Each archive may appear at most once in a rollup's source list.
/// </param>
/// <param name="ValidFrom">
/// Inclusive start of the span; <c>null</c> ⇒ open start (the source covers every bucket before
/// <see cref="ValidTo"/>).
/// </param>
/// <param name="ValidTo">
/// Exclusive end of the span; <c>null</c> ⇒ open end (the source covers every bucket from
/// <see cref="ValidFrom"/> onwards).
/// </param>
public sealed record RollupSourceReference(
    OctoObjectId SourceArchiveRtId,
    DateTime? ValidFrom = null,
    DateTime? ValidTo = null)
{
    /// <summary>
    /// True when neither bound is set — the source is authoritative for every bucket. The storage
    /// form the deprecated single <c>SourceArchiveRtId</c> scalar normalises into.
    /// </summary>
    public bool IsUnbounded => ValidFrom is null && ValidTo is null;

    /// <summary>
    /// True when the half-open bucket <c>[<paramref name="bucketStart"/>,
    /// <paramref name="bucketEnd"/>)</c> lies <em>entirely</em> inside this reference's span. A
    /// bucket that only partially overlaps the span is not covered — the aggregation of a bucket
    /// must never mix two sources.
    /// </summary>
    /// <param name="bucketStart">Inclusive start of the bucket.</param>
    /// <param name="bucketEnd">Exclusive end of the bucket.</param>
    public bool Contains(DateTime bucketStart, DateTime bucketEnd) =>
        (ValidFrom is not { } validFrom || bucketStart >= validFrom) &&
        (ValidTo is not { } validTo || bucketEnd <= validTo);

    /// <summary>
    /// Intersects the half-open range <c>[<paramref name="from"/>, <paramref name="to"/>)</c> with
    /// this reference's span and returns the clipped range, or <c>null</c> when the intersection is
    /// empty. Used by the recompute orchestrator to restrict a dirty range to the span of the
    /// source that produced it — on both ends.
    /// </summary>
    /// <param name="from">Inclusive start of the range to clip.</param>
    /// <param name="to">Exclusive end of the range to clip.</param>
    public (DateTime From, DateTime To)? Clip(DateTime from, DateTime to)
    {
        var clippedFrom = ValidFrom is { } validFrom && validFrom > from ? validFrom : from;
        var clippedTo = ValidTo is { } validTo && validTo < to ? validTo : to;

        return clippedFrom < clippedTo ? (clippedFrom, clippedTo) : null;
    }
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
/// <param name="ComparisonValue">
/// State literal a <see cref="CkRollupFunction.StateDuration"/> aggregation matches the source
/// column against — a number (<c>"2"</c>, <c>"100"</c>), a boolean (<c>"true"</c>/<c>"false"</c>)
/// or a string state name. Required for StateDuration; ignored for every other function. AB#4336.
/// </param>
public sealed record CkRollupAggregationSpec(
    string SourcePath,
    CkRollupFunction Function,
    string? TargetColumnName,
    string? ComparisonValue = null);
