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
/// A schema-relevant change (SourceArchiveRtId, BucketSize, Aggregations) was attempted on a
/// rollup that has already left <see cref="CkArchiveStatus.Created"/>. Mutate WatermarkLag /
/// FrozenUntil instead; recreate the rollup for schema changes. Concept §7, §10.
/// </summary>
public sealed class RollupSchemaImmutableException : StreamDataException
{
    public RollupSchemaImmutableException(OctoObjectId rollupArchiveRtId, CkArchiveStatus currentStatus)
        : base($"Rollup archive '{rollupArchiveRtId}' is in status {currentStatus}; SourceArchiveRtId, BucketSize, and Aggregations are frozen.", rollupArchiveRtId) { }
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
/// interval are not checked.
/// </summary>
public sealed class RollupBucketIntervalException : StreamDataException
{
    public TimeSpan BucketSize { get; }
    public TimeSpan SourceGranularity { get; }

    public RollupBucketIntervalException(OctoObjectId rollupArchiveRtId, TimeSpan bucketSize, TimeSpan sourceGranularity)
        : base(
            $"Rollup archive '{rollupArchiveRtId}' bucket interval ({FormatInterval(bucketSize)}) must be greater " +
            $"than or equal to and an integer multiple of the source granularity ({FormatInterval(sourceGranularity)}).",
            rollupArchiveRtId)
    {
        BucketSize = bucketSize;
        SourceGranularity = sourceGranularity;
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
