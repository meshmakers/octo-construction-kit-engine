using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// A single externally-aggregated time-series data point that covers a half-open
/// <c>[From, To)</c> window. Counterpart of <see cref="StreamDataPoint"/> for archives that
/// store pre-computed window values (EDA energy reports, smart-meter quarter-hour totals,
/// weather forecasts with validity periods, …). The window itself is part of the data — it
/// is not derived from a bucket schedule and is not the system's choice. Concept doc:
/// <c>docs/concept-time-range-archives.md</c>.
/// </summary>
/// <remarks>
/// Both <see cref="From"/> and <see cref="To"/> must be UTC; the repository normalises
/// non-UTC inputs the same way <see cref="StreamDataPoint.Timestamp"/> is handled today.
/// <see cref="To"/> is exclusive; <see cref="From"/> is inclusive. Same-window re-deliveries
/// upsert via the natural key <c>(window_start, window_end, rtid, ckTypeId)</c> and set the
/// archive's <c>was_updated</c> flag.
/// </remarks>
public class TimeRangeStreamDataPoint
{
    /// <summary>Runtime id of the entity this measurement belongs to.</summary>
    public required OctoObjectId RtId { get; init; }

    /// <summary>CK type of the entity. Must match the archive's <c>TargetCkTypeId</c>.</summary>
    public required RtCkId<CkTypeId> CkTypeId { get; init; }

    /// <summary>Inclusive lower bound of the window (UTC).</summary>
    public required DateTime From { get; init; }

    /// <summary>Exclusive upper bound of the window (UTC). Must be strictly greater than <see cref="From"/>.</summary>
    public required DateTime To { get; init; }

    /// <summary>
    /// Optional human-readable name. Mapped 1:1 to the archive's <c>rtwellknownname</c> column
    /// — the storage layer uses <c>MAX(rtwellknownname)</c> on re-deliveries.
    /// </summary>
    public string? RtWellKnownName { get; init; }

    /// <summary>
    /// User-column values keyed by attribute path. Unknown keys are dropped with a WARN log,
    /// matching the policy for <see cref="StreamDataPoint.Attributes"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; }
        = new Dictionary<string, object?>();
}
