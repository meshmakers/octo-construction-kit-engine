using System;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Information B of the recompute model (AB#4184): one pending recompute obligation, derived by
/// propagating an <see cref="ArchiveDirtyWindow"/> through the rollup dependency graph. Identifies a
/// dependent archive and the bucket-aligned half-open range <c>[RangeStart, RangeEnd)</c> to
/// recompute, optionally scoped to a single <see cref="RtIdScope"/> (metering point / stream). Maps
/// 1:1 to the <c>CkArchiveRecomputeRange</c> record. All timestamps are UTC.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RtIdScope"/> is <c>null</c> for ranges derived from dirty-window propagation (which is
/// window- not rtId-granular); it is only populated when an operator triggers a manually scoped
/// recompute. The range boundaries are aligned to the dependent's own bucket boundaries, so the
/// recompute orchestrator can process them as whole buckets.
/// </para>
/// <para>
/// <b>Attempt tracking (AB#5189).</b> A range that fails is re-enqueued so a later pass finishes it,
/// but a permanently broken rollup must not consume a recompute run every tick forever.
/// <see cref="Attempts"/> counts the failures this obligation has already accumulated,
/// <see cref="NextAttemptAt"/> holds it back until the backoff has elapsed, and
/// <see cref="LastError"/> carries the reason of the most recent failure. A freshly enqueued range
/// is <c>(0, null, null)</c> — the pre-1.9.0 shape — so ranges stored before the upgrade simply
/// start at attempt zero.
/// </para>
/// </remarks>
public sealed record ArchiveRecomputeRange(
    OctoObjectId DependentArchiveRtId,
    DateTime RangeStart,
    DateTime RangeEnd,
    OctoObjectId? RtIdScope,
    DateTime EnqueuedAt,
    int Attempts = 0,
    DateTime? NextAttemptAt = null,
    string? LastError = null)
{
    /// <summary>
    /// Whether the drain may pick this obligation up at <paramref name="now"/>. A range that has
    /// never failed (<see cref="NextAttemptAt"/> <c>null</c>) is always due.
    /// </summary>
    public bool IsDueAt(DateTime now) => NextAttemptAt is not { } next || next <= now;
}
