#pragma warning disable CS1591 // Missing XML docs on rollup exception members
using System;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The rollup archive references a source archive that does not exist (or has been soft-deleted).
/// Concept §10.
/// </summary>
public sealed class RollupSourceMissingException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }

    public RollupSourceMissingException(OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId)
        : base($"Rollup archive '{rollupArchiveRtId}' references source archive '{sourceArchiveRtId}', which does not exist.", rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
    }
}

/// <summary>
/// Activation was attempted on a rollup archive whose source archive is not in
/// <see cref="CkArchiveStatus.Activated"/>. Concept §10.
/// </summary>
public sealed class RollupSourceNotActivatedException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }
    public CkArchiveStatus SourceStatus { get; }

    public RollupSourceNotActivatedException(
        OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId, CkArchiveStatus sourceStatus)
        : base($"Cannot activate rollup '{rollupArchiveRtId}': source archive '{sourceArchiveRtId}' is in status {sourceStatus}; required: Activated.", rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        SourceStatus = sourceStatus;
    }
}

/// <summary>
/// One of the rollup's <c>Aggregations[].SourcePath</c> entries does not resolve against the source
/// archive's captured column list, or the source column's primitive type is incompatible with the
/// requested aggregation function (e.g. AVG/MIN/MAX/SUM on a non-numeric column). Concept §10.
/// Not thrown by the engine since AB#5157 (superseded by
/// <see cref="RollupSourcePathMissingException"/>, which names the offending source); kept for
/// binary compatibility.
/// </summary>
public sealed class RollupSourcePathInvalidException : StreamDataException
{
    public string SourcePath { get; }

    public RollupSourcePathInvalidException(OctoObjectId rollupArchiveRtId, string sourcePath, string reason)
        : base($"Rollup '{rollupArchiveRtId}' source path '{sourcePath}' is invalid: {reason}", rollupArchiveRtId)
    {
        SourcePath = sourcePath;
    }
}

/// <summary>
/// The rollup's <c>Aggregations</c> list is empty. At least one aggregation is required.
/// Concept §10.
/// </summary>
public sealed class RollupAggregationsRequiredException : StreamDataException
{
    public RollupAggregationsRequiredException(OctoObjectId rollupArchiveRtId)
        : base($"Rollup archive '{rollupArchiveRtId}' must define at least one aggregation.", rollupArchiveRtId) { }
}

/// <summary>
/// Two entries in <c>Aggregations</c> share the same <c>(SourcePath, Function)</c> pair. Concept §10.
/// </summary>
public sealed class DuplicateRollupAggregationException : StreamDataException
{
    public string SourcePath { get; }
    public CkRollupFunction Function { get; }

    public DuplicateRollupAggregationException(
        OctoObjectId rollupArchiveRtId, string sourcePath, CkRollupFunction function)
        : base($"Rollup archive '{rollupArchiveRtId}' has duplicate aggregation '{function}' on '{sourcePath}'.", rollupArchiveRtId)
    {
        SourcePath = sourcePath;
        Function = function;
    }
}

/// <summary>
/// A schema-relevant change (Sources, BucketSize, Aggregations) was attempted on a
/// rollup that has already left <see cref="CkArchiveStatus.Created"/>. Mutate WatermarkLag /
/// FrozenUntil instead; recreate the rollup for schema changes. Concept §7, §10.
/// Reserved for a future guard: the rule is documented, not enforced (AB#5157 decision 9), so the
/// engine does not throw this today.
/// </summary>
public sealed class RollupSchemaImmutableException : StreamDataException
{
    public RollupSchemaImmutableException(OctoObjectId rollupArchiveRtId, CkArchiveStatus currentStatus)
        : base($"Rollup archive '{rollupArchiveRtId}' is in status {currentStatus}; Sources, BucketSize, and Aggregations are frozen.", rollupArchiveRtId) { }
}

/// <summary>
/// A <see cref="CkRollupFunction.StateDuration"/> aggregation is missing its
/// <c>ComparisonValue</c> — without a state literal there is nothing to measure the duration of.
/// AB#4336.
/// </summary>
public sealed class RollupComparisonValueRequiredException : StreamDataException
{
    public string SourcePath { get; }

    public RollupComparisonValueRequiredException(OctoObjectId rollupArchiveRtId, string sourcePath)
        : base($"Rollup archive '{rollupArchiveRtId}': the StateDuration aggregation on '{sourcePath}' requires a ComparisonValue.", rollupArchiveRtId)
    {
        SourcePath = sourcePath;
    }
}

/// <summary>
/// The rollup chain forms a cycle (rollup references itself, directly or transitively). Concept §10.
/// </summary>
public sealed class RollupCycleException : StreamDataException
{
    public RollupCycleException(OctoObjectId rollupArchiveRtId)
        : base($"Rollup archive '{rollupArchiveRtId}' would form a cycle in the source chain.", rollupArchiveRtId) { }
}

/// <summary>
/// The rollup's target bucket interval is finer than — or not an integer multiple of — the source
/// archive's native window length (AB#4289). A finer or misaligned bucket is effectively
/// upsampling and always a configuration mistake: a raw source yields a sparse table (only buckets
/// that happen to contain a source row materialise), a windowed source (TimeRange / rollup) yields
/// an empty one (no source window is fully contained in a smaller target bucket). Enforced at
/// activation only when the source granularity is known; raw archives with an undeclared sampling
/// interval are not checked. For a calendar-aligned rollup the same exception carries the
/// alignment-nesting violation (AB#5157: a source whose calendar buckets are coarser than the
/// rollup's), in which case <see cref="SourceAlignment"/> is set and the window lengths are zero.
/// </summary>
public sealed class RollupBucketIntervalException : StreamDataException
{
    public TimeSpan BucketSize { get; }
    public TimeSpan SourceGranularity { get; }

    /// <summary>
    /// The offending source archive, or <c>null</c> when the violation was raised without one
    /// (the pre-AB#5157 constructor). AB#5157: with several sources the operator needs to know
    /// which one failed the rule.
    /// </summary>
    public OctoObjectId? SourceArchiveRtId { get; }

    /// <summary>
    /// The offending source's bucket alignment when the rule that failed was calendar nesting,
    /// <c>null</c> for the window-length rules. AB#5157.
    /// </summary>
    public BucketAlignment? SourceAlignment { get; }

    public RollupBucketIntervalException(OctoObjectId rollupArchiveRtId, TimeSpan bucketSize, TimeSpan sourceGranularity)
        : base(
            $"Rollup archive '{rollupArchiveRtId}' bucket interval ({FormatInterval(bucketSize)}) must be greater " +
            $"than or equal to and an integer multiple of the source granularity ({FormatInterval(sourceGranularity)}).",
            rollupArchiveRtId)
    {
        BucketSize = bucketSize;
        SourceGranularity = sourceGranularity;
    }

    /// <summary>
    /// The window-length rule failed for a named source (AB#5157): the rollup's bucket interval is
    /// finer than, or not an integer multiple of, that source's window length.
    /// </summary>
    public RollupBucketIntervalException(
        OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId, TimeSpan bucketSize, TimeSpan sourceGranularity)
        : base(
            $"Rollup archive '{rollupArchiveRtId}' bucket interval ({FormatInterval(bucketSize)}) must be greater " +
            $"than or equal to and an integer multiple of the window length ({FormatInterval(sourceGranularity)}) " +
            $"of source archive '{sourceArchiveRtId}'.",
            rollupArchiveRtId)
    {
        BucketSize = bucketSize;
        SourceGranularity = sourceGranularity;
        SourceArchiveRtId = sourceArchiveRtId;
    }

    /// <summary>
    /// The calendar-nesting rule failed for a named source (AB#5157): the source's calendar buckets
    /// do not nest inside the rollup's calendar buckets. <c>BucketSize</c> is informational for
    /// calendar-aligned rollups, so it plays no part in the message.
    /// </summary>
    public RollupBucketIntervalException(
        OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId,
        BucketAlignment rollupAlignment, BucketAlignment sourceAlignment)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': source archive '{sourceArchiveRtId}' is aligned " +
            $"{sourceAlignment}, whose buckets do not nest inside the rollup's {rollupAlignment} buckets.",
            rollupArchiveRtId)
    {
        BucketSize = TimeSpan.Zero;
        SourceGranularity = TimeSpan.Zero;
        SourceArchiveRtId = sourceArchiveRtId;
        SourceAlignment = sourceAlignment;
    }

    /// <summary>Renders a TimeSpan in its most natural whole unit (d / h / min / s) for the message.</summary>
    private static string FormatInterval(TimeSpan value)
    {
        if (value.Ticks % TimeSpan.TicksPerDay == 0 && value.TotalDays >= 1)
        {
            return $"{value.TotalDays:0} d";
        }
        if (value.Ticks % TimeSpan.TicksPerHour == 0 && value.TotalHours >= 1)
        {
            return $"{value.TotalHours:0} h";
        }
        if (value.Ticks % TimeSpan.TicksPerMinute == 0 && value.TotalMinutes >= 1)
        {
            return $"{value.TotalMinutes:0} min";
        }
        return $"{value.TotalSeconds:0.###} s";
    }
}

/// <summary>
/// A source archive deletion was attempted while at least one non-soft-deleted rollup references
/// it. The operator must delete or freeze the rollups first; this prevents accidental destruction
/// of aggregated history. Concept §6, §10.
/// </summary>
public sealed class RollupSourceInUseException : StreamDataException
{
    public int DependentRollupCount { get; }

    public RollupSourceInUseException(OctoObjectId sourceArchiveRtId, int dependentRollupCount)
        : base($"Source archive '{sourceArchiveRtId}' has {dependentRollupCount} active rollup(s) attached. Delete or freeze them first.", sourceArchiveRtId)
    {
        DependentRollupCount = dependentRollupCount;
    }
}

// ---------- Multi-source declaration (AB#5157, System.StreamData 1.8.0) ----------
// One distinct exception type per rule so a caller can react to the individual violation, and
// every message names the offending source archive. The rules run at create/save time and are
// re-checked at activation.

/// <summary>
/// The rollup declares no source at all — neither a <c>Sources</c> entry nor the deprecated
/// <c>SourceArchiveRtId</c> scalar. At least one source is required. AB#5157.
/// </summary>
public sealed class RollupSourcesRequiredException : StreamDataException
{
    public RollupSourcesRequiredException(OctoObjectId rollupArchiveRtId)
        : base($"Rollup archive '{rollupArchiveRtId}' must declare at least one source archive.", rollupArchiveRtId) { }
}

/// <summary>
/// The rollup carries both storage forms and they disagree: the deprecated
/// <c>SourceArchiveRtId</c> scalar is set while <c>Sources</c> declares something other than
/// exactly one unbounded reference to that same archive. The normalisation point keeps
/// <c>Sources</c> and flags the conflict on the snapshot rather than guessing; activation rejects
/// it here. AB#5157.
/// </summary>
public sealed class RollupSourceDeclarationConflictException : StreamDataException
{
    public OctoObjectId DeprecatedSourceArchiveRtId { get; }
    public OctoObjectId FirstSourceArchiveRtId { get; }

    public RollupSourceDeclarationConflictException(
        OctoObjectId rollupArchiveRtId, OctoObjectId deprecatedSourceArchiveRtId, OctoObjectId firstSourceArchiveRtId)
        : base(
            $"Rollup archive '{rollupArchiveRtId}' declares the deprecated SourceArchiveRtId '{deprecatedSourceArchiveRtId}' " +
            $"and a conflicting Sources list starting with source archive '{firstSourceArchiveRtId}'. " +
            "Remove SourceArchiveRtId and declare every source in Sources.",
            rollupArchiveRtId)
    {
        DeprecatedSourceArchiveRtId = deprecatedSourceArchiveRtId;
        FirstSourceArchiveRtId = firstSourceArchiveRtId;
    }
}

/// <summary>
/// The same source archive appears more than once in the rollup's <c>Sources</c> list. Each source
/// archive may be referenced at most once, whatever validity spans the entries carry. AB#5157.
/// </summary>
public sealed class DuplicateRollupSourceException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }

    public DuplicateRollupSourceException(OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId)
        : base($"Rollup archive '{rollupArchiveRtId}' references source archive '{sourceArchiveRtId}' more than once.", rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
    }
}

/// <summary>
/// A source's validity span is inverted or empty — <c>ValidFrom</c> is at or after
/// <c>ValidTo</c>, so the span covers no bucket at all. AB#5157.
/// </summary>
public sealed class RollupSourceSpanInvertedException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }
    public DateTime ValidFrom { get; }
    public DateTime ValidTo { get; }

    public RollupSourceSpanInvertedException(
        OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId, DateTime validFrom, DateTime validTo)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': the validity span of source archive '{sourceArchiveRtId}' is empty — " +
            $"ValidFrom {validFrom:o} must be earlier than ValidTo {validTo:o}.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }
}

/// <summary>
/// Two sources' validity spans overlap. Spans must be pairwise disjoint so every bucket is served
/// by at most one source; abutting spans (one's <c>ValidTo</c> equals the other's
/// <c>ValidFrom</c>) are disjoint because the intervals are half-open. AB#5157.
/// </summary>
public sealed class RollupSourceSpanOverlapException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }
    public OctoObjectId OverlappingSourceArchiveRtId { get; }

    public RollupSourceSpanOverlapException(
        OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId, OctoObjectId overlappingSourceArchiveRtId)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': the validity span of source archive '{sourceArchiveRtId}' overlaps " +
            $"the span of source archive '{overlappingSourceArchiveRtId}'. Source spans must be pairwise disjoint.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        OverlappingSourceArchiveRtId = overlappingSourceArchiveRtId;
    }
}

/// <summary>
/// More than one source leaves the same span end open: at most one source may have an open start
/// (no <c>ValidFrom</c>) and at most one an open end (no <c>ValidTo</c>), otherwise the open sides
/// would necessarily overlap. AB#5157.
/// </summary>
public sealed class RollupSourceSpanOpenEndConflictException : StreamDataException
{
    /// <summary>True when the conflict is about two open starts; false for two open ends.</summary>
    public bool IsOpenStart { get; }

    public OctoObjectId SourceArchiveRtId { get; }
    public OctoObjectId ConflictingSourceArchiveRtId { get; }

    public RollupSourceSpanOpenEndConflictException(
        OctoObjectId rollupArchiveRtId,
        OctoObjectId sourceArchiveRtId,
        OctoObjectId conflictingSourceArchiveRtId,
        bool isOpenStart)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': source archive '{sourceArchiveRtId}' and source archive " +
            $"'{conflictingSourceArchiveRtId}' both leave the {(isOpenStart ? "start" : "end")} of their validity span open. " +
            $"At most one source may have an open {(isOpenStart ? "start (no ValidFrom)" : "end (no ValidTo)")}.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        ConflictingSourceArchiveRtId = conflictingSourceArchiveRtId;
        IsOpenStart = isOpenStart;
    }
}

/// <summary>
/// A validity-span boundary does not lie on a bucket boundary of the rollup, evaluated in the
/// rollup's reference time zone. Off-grid boundaries would split a bucket between two sources — or
/// leave it uncovered by either — so they are rejected. AB#5157.
/// </summary>
public sealed class RollupSourceSpanNotOnBucketBoundaryException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }

    /// <summary>Name of the offending bound, <c>ValidFrom</c> or <c>ValidTo</c>.</summary>
    public string BoundaryName { get; }

    public DateTime Boundary { get; }

    public RollupSourceSpanNotOnBucketBoundaryException(
        OctoObjectId rollupArchiveRtId,
        OctoObjectId sourceArchiveRtId,
        string boundaryName,
        DateTime boundary)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': {boundaryName} {boundary:o} of source archive '{sourceArchiveRtId}' " +
            "does not lie on a bucket boundary of the rollup.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        BoundaryName = boundaryName;
        Boundary = boundary;
    }
}

/// <summary>
/// A source archive targets a different CK type than the rollup. Every source must materialise the
/// same target type, otherwise the rollup's rows would describe two different entity populations.
/// AB#5157.
/// </summary>
public sealed class RollupSourceTargetTypeMismatchException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }
    public RtCkId<CkTypeId> ExpectedTargetCkTypeId { get; }
    public RtCkId<CkTypeId> ActualTargetCkTypeId { get; }

    public RollupSourceTargetTypeMismatchException(
        OctoObjectId rollupArchiveRtId,
        OctoObjectId sourceArchiveRtId,
        RtCkId<CkTypeId> expectedTargetCkTypeId,
        RtCkId<CkTypeId> actualTargetCkTypeId)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': source archive '{sourceArchiveRtId}' targets CK type " +
            $"'{actualTargetCkTypeId}', but the rollup targets '{expectedTargetCkTypeId}'. All sources must target the same CK type.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        ExpectedTargetCkTypeId = expectedTargetCkTypeId;
        ActualTargetCkTypeId = actualTargetCkTypeId;
    }
}

/// <summary>
/// An aggregation's <c>SourcePath</c> does not resolve against one of the rollup's source
/// archives. Every path must resolve on <em>every</em> source (strict), otherwise the rollup would
/// silently produce empty columns for the buckets that source serves. AB#5157.
/// </summary>
public sealed class RollupSourcePathMissingException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }
    public string SourcePath { get; }

    public RollupSourcePathMissingException(
        OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId, string sourcePath)
        : base(
            $"Rollup archive '{rollupArchiveRtId}': aggregation source path '{sourcePath}' does not resolve on " +
            $"source archive '{sourceArchiveRtId}'. Every aggregation path must resolve on every source.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
        SourcePath = sourcePath;
    }
}

/// <summary>
/// The declared sources close a cycle in the rollup graph — the rollup reaches itself again by
/// following source edges, directly or transitively. Checked over all source edges at create time
/// and again at activation. AB#5157.
/// </summary>
public sealed class RollupSourceCycleException : StreamDataException
{
    public OctoObjectId SourceArchiveRtId { get; }

    public RollupSourceCycleException(OctoObjectId rollupArchiveRtId, OctoObjectId sourceArchiveRtId)
        : base(
            $"Rollup archive '{rollupArchiveRtId}' would form a cycle in the source graph through source archive " +
            $"'{sourceArchiveRtId}'.",
            rollupArchiveRtId)
    {
        SourceArchiveRtId = sourceArchiveRtId;
    }
}
