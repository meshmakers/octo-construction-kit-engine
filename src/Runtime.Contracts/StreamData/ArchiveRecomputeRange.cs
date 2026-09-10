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
/// <b>Attempt tracking (AB#5189).</b> A range that fails is kept so a later pass finishes it, but a
/// permanently broken rollup must not consume a recompute run every tick forever.
/// <see cref="Attempts"/> counts the failures this obligation has already accumulated,
/// <see cref="NextAttemptAt"/> holds it back until the backoff has elapsed, and
/// <see cref="LastError"/> carries the reason of the most recent failure. A freshly enqueued range
/// is <c>(0, null, null)</c> — the pre-1.9.0 shape — so ranges stored before the upgrade simply
/// start at attempt zero. Once the attempt cap is reached the obligation is <em>parked</em>
/// (<see cref="IsParked"/>): it stays on the entity, visibly, and is never due again until a
/// recompute that covers it succeeds and releases it.
/// </para>
/// <para>
/// <b>Identity.</b> The record is a value: two obligations are the same only when every field
/// matches, and a store removes an obligation by handing back the very record it read. That is
/// deliberate — an identical range enqueued <em>while</em> a run was in progress (a manual trigger
/// coalesced into the running job) is a newer obligation with a later <see cref="EnqueuedAt"/>, and
/// the run that finished cannot have honoured it.
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
    /// The <see cref="NextAttemptAt"/> a parked obligation carries (AB#5189): far enough in the
    /// future never to be due, and far enough from <see cref="DateTime.MaxValue"/> to survive a
    /// millisecond-resolution store round trip unchanged in meaning. Compared with <c>&gt;=</c>, so
    /// a store that truncates it still reads back as parked.
    /// </summary>
    public static readonly DateTime ParkedUntil = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Whether the drain may pick this obligation up at <paramref name="now"/>. A range that has
    /// never failed (<see cref="NextAttemptAt"/> <c>null</c>) is always due; a parked one never is.
    /// </summary>
    public bool IsDueAt(DateTime now) => NextAttemptAt is not { } next || next <= now;

    /// <summary>
    /// Whether this obligation has exhausted its attempts and waits, visibly, for the cause to be
    /// fixed and a covering recompute to succeed (AB#5189).
    /// </summary>
    public bool IsParked => NextAttemptAt is { } next && next >= ParkedUntil;

    /// <summary>Whether <c>[RangeStart, RangeEnd)</c> and <c>[from, to)</c> share at least one instant.</summary>
    public bool Overlaps(DateTime from, DateTime to) => RangeStart < to && from < RangeEnd;

    /// <summary>Whether <c>[RangeStart, RangeEnd)</c> lies entirely inside <c>[from, to)</c>.</summary>
    public bool IsContainedIn(DateTime from, DateTime to) => RangeStart >= from && RangeEnd <= to;
}
