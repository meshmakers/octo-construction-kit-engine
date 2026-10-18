#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The outcome of dumping one archive with <see cref="IStreamDataRepository.DumpArchiveToStagingAsync"/>: the plan the
/// files belong to (the one of the last attempt), the result of every slice, and — for a rollup — how the repeat rule
/// of the generation map went (AB#5967, concept §6).
/// </summary>
/// <param name="ArchiveRtId">The archive that was dumped.</param>
/// <param name="Plan">The plan of the attempt whose files stand; the bot writes it into the manifest.</param>
/// <param name="Slices">Result of each slice of <paramref name="Plan"/>, in copy order.</param>
/// <param name="Attempts">How often the rollup was copied; 1 unless its generation map moved while it was copied. 1 for any other archive.</param>
/// <param name="MapReadAtUtc">
/// When the generation map was read for the attempt that stands (the map is read before the data, so this is never
/// later than <paramref name="LastDataCopiedAtUtc"/>); <c>null</c> for an archive without a generation map.
/// </param>
/// <param name="LastDataCopiedAtUtc">When the last data slice of that attempt was copied; <c>null</c> when no slice was copied.</param>
public sealed record ArchiveCopyDumpResult(
    OctoObjectId ArchiveRtId,
    ArchiveCopyPlan Plan,
    IReadOnlyList<ArchiveCopySliceResult> Slices,
    int Attempts,
    DateTime? MapReadAtUtc,
    DateTime? LastDataCopiedAtUtc)
{
    /// <summary>True when every slice of the plan was copied.</summary>
    public bool IsSuccess => Slices.Count == Plan.Slices.Count && Slices.All(s => s.IsSuccess);
}

/// <summary>
/// The outcome of loading one archive with <see cref="IStreamDataRepository.LoadArchiveFromStagingAsync"/>.
/// </summary>
/// <param name="ArchiveRtId">The archive that was loaded.</param>
/// <param name="Slices">Result of each slice that was attempted.</param>
/// <param name="ExpectedRows">The rows the plan counted over all its slices.</param>
/// <param name="LoadedRows">The rows the tables hold after the load; <c>null</c> when the load did not get that far.</param>
/// <param name="RowCountVerified">
/// True when the tables hold exactly the counted rows (data table and, for a rollup, generation map). False when
/// they differ or when the load stopped early; the restore job treats it as a failed archive.
/// </param>
public sealed record ArchiveCopyLoadResult(
    OctoObjectId ArchiveRtId,
    IReadOnlyList<ArchiveCopySliceResult> Slices,
    long ExpectedRows,
    long? LoadedRows,
    bool RowCountVerified)
{
    /// <summary>True when every slice was loaded and the row counts are verified.</summary>
    public bool IsSuccess => RowCountVerified && Slices.All(s => s.IsSuccess);
}
