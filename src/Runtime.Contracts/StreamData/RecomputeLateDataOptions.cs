using System;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// How the platform recomputes rollups after a late stream-data delivery (AB#9016). Bound from the
/// <see cref="SectionName"/> configuration section, which also holds the CrateDB recompute tunables
/// (<c>CrateRecomputeOptions</c>) under other property names.
/// </summary>
public sealed class RecomputeLateDataOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "StreamData:Recompute";

    /// <summary>Default of <see cref="LateDataDebounce"/>: five minutes.</summary>
    public static readonly TimeSpan DefaultLateDataDebounce = TimeSpan.FromMinutes(5);

    /// <summary>Default of <see cref="MaxScopedRtIds"/>.</summary>
    public const int DefaultMaxScopedRtIds = 200;

    /// <summary>
    /// How long the recompute of a rollup waits after the first late delivery before it runs, so that the late
    /// deliveries of that interval are computed in one job (<c>StreamData:Recompute:LateDataDebounce</c>). The state is
    /// the obligation's <c>NextAttemptAt</c> on the archive in MongoDB, so it holds across replicas and restarts.
    /// <see cref="TimeSpan.Zero"/> switches the debounce off (every late delivery is due on the next tick, as before).
    /// Must not be negative. Default five minutes.
    /// </summary>
    public TimeSpan LateDataDebounce { get; set; } = DefaultLateDataDebounce;

    /// <summary>
    /// The most rtIds a late-delivery recompute is restricted to (<c>StreamData:Recompute:MaxScopedRtIds</c>). A write
    /// with more distinct rtIds, or a merged interval whose sets together hold more, falls back to the unscoped recompute
    /// of every series. 0 switches the scoped recompute off. Must not be negative. Default 200.
    /// </summary>
    public int MaxScopedRtIds { get; set; } = DefaultMaxScopedRtIds;

    /// <summary>True when the values are usable.</summary>
    public bool IsValid() => LateDataDebounce >= TimeSpan.Zero && MaxScopedRtIds >= 0;

    /// <summary>The message for values <see cref="IsValid"/> rejects.</summary>
    public const string ValidationMessage =
        "StreamData:Recompute:LateDataDebounce must be zero (off) or a positive time span such as 00:05:00 and " +
        "StreamData:Recompute:MaxScopedRtIds must be zero (off) or a positive number.";
}
