using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Read-only snapshot of the parts of a <c>CkArchive</c> entity the lifecycle service needs.
/// Intentionally narrower than the full RtEntity: the lifecycle decisions only depend on
/// <see cref="Status"/> plus identification fields. Backend-specific stores translate from their
/// concrete representation to this record. <c>Columns</c> carries the user-configured archive
/// columns (path/indexed/required) the data-store provider needs to generate DDL — empty when the
/// archive only has the standard time-series columns.
/// </summary>
public sealed record ArchiveSnapshot(
    OctoObjectId RtId,
    RtCkId<CkTypeId> TargetCkTypeId,
    CkArchiveStatus Status,
    string? RtWellKnownName,
    IReadOnlyList<CkArchiveColumnSpec> Columns)
{
    /// <summary>
    /// When this snapshot represents a <c>RollupArchive</c>, the aggregation specs from which
    /// <see cref="Columns"/> were derived (via <see cref="RollupColumnGenerator"/>). Null for raw
    /// and time-range archives. The DDL path uses this to skip CK-type attribute resolution for
    /// rollups — the derived column names (e.g. <c>temperature_avg_sum</c>) are storage
    /// identifiers, not paths into the CK type, so the column SQL type is determined by the
    /// aggregation function instead. Concept §4.
    /// </summary>
    public IReadOnlyList<CkRollupAggregationSpec>? RollupAggregations { get; init; }

    /// <summary>
    /// True when this snapshot represents a <c>TimeRangeArchive</c> — each row carries an
    /// explicit <c>[window_start, window_end)</c> instead of a single <c>timestamp</c>. The DDL
    /// path emits two timestamp columns + a <c>was_updated</c> flag column, and the natural key
    /// becomes <c>(window_start, window_end, rtid, ckTypeId)</c>. Concept §4 + §6.
    /// </summary>
    /// <remarks>
    /// Discriminator semantics: <see cref="RollupAggregations"/> non-null ⇒ rollup;
    /// <see cref="IsTimeRange"/> true ⇒ time-range; both false ⇒ raw archive. Rollups and
    /// time-range archives are mutually exclusive at the storage shape level (a rollup
    /// snapshot has IsTimeRange = false today; the rollup-on-time-range unification ships
    /// in Phase 7).
    /// </remarks>
    public bool IsTimeRange { get; init; }

    /// <summary>
    /// Advisory period for a <c>TimeRangeArchive</c>'s windows (e.g. 15 min, 1 h, 1 d).
    /// Optional and descriptive only — the engine does not enforce that incoming windows match
    /// the declared period. Null for raw and rollup archives.
    /// </summary>
    public System.TimeSpan? Period { get; init; }
}

/// <summary>
/// Minimal projection of a <c>CkArchive.columns[]</c> entry — just enough for the data-store
/// provider to resolve types via the CK model and generate DDL. The CK record carries more fields
/// than this in some models, but the lifecycle service only cares about path / indexed / required.
/// </summary>
public sealed record CkArchiveColumnSpec(
    string Path,
    bool Indexed,
    bool Required);
