#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

public enum ArchiveCopySliceStatus
{
    Succeeded = 0,
    Failed = 1
}

/// <summary>
/// What happened to one slice of a native copy. The bot writes these into the dump manifest and the job log.
/// </summary>
/// <param name="Slice">The slice; <see cref="ArchiveCopySlice.RowCount"/> is the number counted before the dump.</param>
/// <param name="Status">
/// <see cref="ArchiveCopySliceStatus.Failed"/> for a slice that did not copy (store unreachable, wrong key, rows
/// rejected on load). A difference between the counted and the copied rows of a <b>dump</b> is no failure — the
/// tenant stays online, the open bucket changes while the copy runs — it is reported in
/// <see cref="CopiedRows"/> and <see cref="Message"/>.
/// </param>
/// <param name="CopiedRows">Rows the <c>COPY</c> statement reported (<c>COPY TO</c>: written, <c>COPY FROM</c>: <c>success_count</c>).</param>
/// <param name="ErrorRows">Rows a load rejected (<c>error_count</c>); always 0 for a dump.</param>
/// <param name="Duration">Wall-clock time of the statement.</param>
/// <param name="Message">
/// <b>Redacted</b> text: the failure reason of a failed slice, or a warning on a succeeded one (counted and
/// copied rows differ). Implementations pass every text that may echo the statement through
/// <see cref="ArchiveCopyStagingLocation.Scrub"/>; it never contains a key, secret or SAS token.
/// </param>
public sealed record ArchiveCopySliceResult(
    ArchiveCopySlice Slice,
    ArchiveCopySliceStatus Status,
    long CopiedRows,
    long ErrorRows,
    TimeSpan Duration,
    string? Message = null)
{
    [JsonIgnore]
    public bool IsSuccess => Status == ArchiveCopySliceStatus.Succeeded;

    /// <summary>Copied minus counted rows; 0 when the slice holds exactly what was counted.</summary>
    [JsonIgnore]
    public long RowCountDifference => CopiedRows - Slice.RowCount;
}
