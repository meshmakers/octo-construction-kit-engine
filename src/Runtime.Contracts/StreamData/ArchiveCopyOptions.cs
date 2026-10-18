#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Tuning of a native archive copy. The defaults are the figures of the local trial in
/// <c>concept-tenant-backup-native-archive-dump.md</c> §12 (slices of about two million rows).
/// </summary>
public sealed record ArchiveCopyOptions
{
    public const long DefaultTargetSliceRows = 2_000_000;
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultOptimizeTimeout = TimeSpan.FromHours(2);

    /// <summary>Rows a time slice aims at when the copy is planned (<see cref="IStreamDataRepository.PlanArchiveCopyAsync"/>).</summary>
    public long TargetSliceRows { get; init; } = DefaultTargetSliceRows;

    /// <summary>
    /// Command timeout of one <c>COPY</c> statement. Copy statements run as maintenance statements with this
    /// timeout of their own, not with the interactive query timeout.
    /// </summary>
    public TimeSpan CommandTimeout { get; init; } = DefaultCommandTimeout;

    /// <summary>
    /// Merge the segments of the loaded table after the last slice of a restore (the longest single step of a
    /// restore, 7 min 40 s for the 115 million row tenant on one node). Switch off to leave the merge to CrateDB.
    /// </summary>
    public bool OptimizeAfterLoad { get; init; } = true;

    /// <summary>Command timeout of the segment merge.</summary>
    public TimeSpan OptimizeTimeout { get; init; } = DefaultOptimizeTimeout;

    /// <summary>
    /// Load with <c>overwrite_duplicates = true</c>, so a slice that is loaded a second time after a failure
    /// replaces its rows instead of adding them again.
    /// </summary>
    public bool OverwriteDuplicates { get; init; }

    /// <exception cref="ArgumentOutOfRangeException">A value is not usable.</exception>
    public void Validate()
    {
        if (TargetSliceRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetSliceRows), "The slice target must be positive.");
        }

        if (CommandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout), "The command timeout must be positive.");
        }

        if (OptimizeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OptimizeTimeout), "The optimize timeout must be positive.");
        }
    }
}
