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
    public const int DefaultMaxMapAttempts = 3;
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

    /// <summary>
    /// How often a rollup may be copied again because its generation map moved while it was copied
    /// (<see cref="IStreamDataRepository.DumpArchiveToStagingAsync"/>). The dump fails when the map moves in every attempt.
    /// </summary>
    public int MaxMapAttempts { get; init; } = DefaultMaxMapAttempts;

    /// <summary>
    /// When <c>false</c> a dump of a rollup copies it once, does not compare the generation map before and after, and returns
    /// normally with <c>Attempts = 1</c> even if a recompute moved the map meanwhile. Default <c>true</c> (the repeat rule).
    /// For characterisation tests that show what the repeat rule prevents; not for production use.
    /// </summary>
    public bool RepeatOnMovedMap { get; init; } = true;

    /// <summary>
    /// Called by <see cref="IStreamDataRepository.DumpArchiveToStagingAsync"/> at the phases of <see cref="ArchiveCopyPhase"/> and
    /// awaited before the dump goes on; <c>null</c> = nothing. For tests and diagnostics (e.g. to run a recompute at an exact moment
    /// of a dump); the arguments are the phase and the number of the attempt (from 1). Exceptions it throws end the dump.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<ArchiveCopyPhase, int, System.Threading.CancellationToken, System.Threading.Tasks.Task>? PhaseObserver { get; init; }

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

        if (MaxMapAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMapAttempts), "At least one attempt is needed.");
        }

        if (OptimizeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(OptimizeTimeout), "The optimize timeout must be positive.");
        }
    }
}

/// <summary>The moments of the dump of a rollup that <see cref="ArchiveCopyOptions.PhaseObserver"/> is told about.</summary>
public enum ArchiveCopyPhase
{
    /// <summary>The generation map of an attempt was read; its data slices are not copied yet.</summary>
    MapRead = 0,

    /// <summary>The last data slice of an attempt was copied; the map is not read a second time yet.</summary>
    DataCopied = 1
}
