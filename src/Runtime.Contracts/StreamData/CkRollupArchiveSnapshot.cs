using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Read-only snapshot of the parts of a <c>CkRollupArchive</c> entity the orchestrator and the
/// lifecycle service need. Extends the data carried by <see cref="CkArchiveSnapshot"/> with rollup-
/// specific fields. Backend-specific stores translate from their concrete representation to this
/// record.
/// </summary>
/// <remarks>
/// Concept §3 / §5. <see cref="LastAggregatedBucketEnd"/> is null before the first orchestrator
/// run; it is advanced exclusively by the orchestrator (per bucket commit) and by the
/// <c>rewindRollupWatermark</c> mutation. <see cref="FrozenUntil"/> is null when the rollup is not
/// frozen; once set it is monotonic.
/// </remarks>
public sealed record CkRollupArchiveSnapshot(
    OctoObjectId RtId,
    RtCkId<CkTypeId> TargetCkTypeId,
    CkArchiveStatus Status,
    string? RtWellKnownName,
    OctoObjectId SourceArchiveRtId,
    TimeSpan BucketSize,
    TimeSpan WatermarkLag,
    DateTime? LastAggregatedBucketEnd,
    IReadOnlyList<CkRollupAggregationSpec> Aggregations,
    DateTime? FrozenUntil);

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
