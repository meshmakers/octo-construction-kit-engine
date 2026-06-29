using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;

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

    /// <summary>
    /// True when this snapshot's storage shape is the windowed
    /// <c>(window_start, window_end, rtid, ckTypeId)</c> + <c>was_updated</c> layout — i.e. either
    /// a rollup archive (Phase 7 unification) or a time-range archive. False ⇒ raw archive with
    /// the single <c>timestamp</c> column. Concept-time-range §4 / §6.
    /// </summary>
    public bool UsesWindowedStorage => IsTimeRange || RollupAggregations is not null;
}

/// <summary>
/// Minimal projection of a <c>CkArchive.columns[]</c> entry — enough for the data-store provider
/// to resolve types via the CK model and generate DDL.
/// <para>
/// A column is either <em>ingested</em> (<see cref="Path"/> set, <see cref="Formula"/> null) or
/// <em>computed</em> (<see cref="Formula"/> set, <see cref="Path"/> empty): a formula referencing
/// other columns of the same row by name, producing a value of <see cref="ResultType"/>. Computed
/// columns arrived with System.StreamData 1.5.0; on older models the computed fields are null.
/// </para>
/// </summary>
public sealed record CkArchiveColumnSpec(
    string Path,
    bool Indexed,
    bool Required)
{
    /// <summary>
    /// Explicit output column name. For a computed column this is the formula-referenceable
    /// identifier (required); for an ingested column it is optional and the name derives from
    /// <see cref="Path"/>.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The formula when this is a computed column (mXparser dialect, see the Formula Expressions
    /// reference). Null for ingested columns. Mutually exclusive with a non-empty <see cref="Path"/>.
    /// </summary>
    public string? Formula { get; init; }

    /// <summary>
    /// Declared output type the formula result is cast back to. Set for computed columns, null for
    /// ingested columns.
    /// </summary>
    public FormulaResultType? ResultType { get; init; }

    /// <summary>
    /// Engine-managed lifecycle state of a computed column. Null for ingested columns.
    /// </summary>
    public ComputedColumnState? ComputedState { get; init; }

    /// <summary>
    /// Engine-managed physical-column version of a computed column (AB#4189 Phase 7). 0 means the
    /// physical column carries the column's base name (the canonical column name derived from
    /// <see cref="Name"/>); a value <c>N &gt; 0</c> means the active physical column is
    /// <c>{base}__v{N}</c> — the result of one or more formula changes that each backfilled into a
    /// fresh versioned physical column and flipped this pointer atomically on completion. 0 for
    /// ingested columns. See <c>System.StreamData</c> 1.6.1.
    /// </summary>
    public int ComputedVersion { get; init; }

    /// <summary>True when this is a computed column (has a <see cref="Formula"/>).</summary>
    public bool IsComputed => !string.IsNullOrWhiteSpace(Formula);
}
