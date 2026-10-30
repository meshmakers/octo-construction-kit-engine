using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Why a row of an archive write was not stored (AB#7598).
/// </summary>
public enum StreamDataSkipReason
{
    /// <summary>
    /// The row's CK type is not the archive's target type. An archive table holds one CK type; rows of another
    /// type are never written.
    /// </summary>
    CkTypeMismatch = 0,

    /// <summary>
    /// The archive's conflict precedence ranks the stored row above this one (for example a measurement
    /// against an estimate). A normal outcome for automated deliveries.
    /// </summary>
    LowerPrecedence = 1,

    /// <summary>
    /// The stored row is a manual value and the archive ranks by origin (<see cref="ArchiveConflictKey.OriginKey" />):
    /// an automated delivery never replaces it.
    /// </summary>
    ManualValueProtected = 2
}

/// <summary>
/// One row of an archive write that was not stored, with the reason (AB#7598).
/// </summary>
/// <param name="RtId">Entity the row belongs to.</param>
/// <param name="From">Timestamp of a raw row, or the inclusive window start of a time-range row.</param>
/// <param name="To">Exclusive window end of a time-range row; <c>null</c> for a raw row.</param>
/// <param name="Origin">Origin the writer declared for the row.</param>
/// <param name="Reason">Why the row was not stored.</param>
public sealed record StreamDataSkippedRow(
    OctoObjectId RtId,
    DateTime From,
    DateTime? To,
    ArchiveRowOrigin Origin,
    StreamDataSkipReason Reason);

/// <summary>
/// What an archive write did (AB#7598): how many rows were stored and which were not, and why. A writer can
/// no longer report success for a value that was not stored.
/// </summary>
/// <remarks>
/// A row is <em>stored</em> when the database holds its values after the write: a new row, an update that won
/// the archive's conflict precedence, or an identical re-delivery. An archive without conflict precedence stores
/// every row of its target type, as before. Rows that lose against a stored row are a normal outcome for
/// automated deliveries and do not fail the write; a manual row that cannot be stored fails it
/// (<see cref="ManualStreamDataNotAppliedException" />).
/// </remarks>
public sealed record StreamDataWriteResult(int Stored, IReadOnlyList<StreamDataSkippedRow> Skipped)
{
    /// <summary>A write of nothing.</summary>
    public static StreamDataWriteResult Empty { get; } = new(0, Array.Empty<StreamDataSkippedRow>());

    /// <summary>Number of rows that were not stored.</summary>
    public int SkippedCount => Skipped.Count;

    /// <summary>Number of rows not stored, per reason; reasons without a row are absent.</summary>
    public IReadOnlyDictionary<StreamDataSkipReason, int> SkippedByReason =>
        Skipped.GroupBy(s => s.Reason).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Combines the results of two parts of one write.</summary>
    public StreamDataWriteResult Add(StreamDataWriteResult other) =>
        new(Stored + other.Stored, Skipped.Concat(other.Skipped).ToList());
}

/// <summary>
/// A manual write could not be stored because the archive's conflict precedence ranks the stored row above it
/// (AB#7598). Raised after the rest of the batch was written; <see cref="Result" /> says what was stored.
/// </summary>
public sealed class ManualStreamDataNotAppliedException : Exception
{
    /// <summary>Creates the exception for the write result.</summary>
    public ManualStreamDataNotAppliedException(OctoObjectId archiveRtId, StreamDataWriteResult result)
        : base(BuildMessage(archiveRtId, result))
    {
        ArchiveRtId = archiveRtId;
        Result = result;
    }

    /// <summary>The archive that was written.</summary>
    public OctoObjectId ArchiveRtId { get; }

    /// <summary>What the write stored and skipped.</summary>
    public StreamDataWriteResult Result { get; }

    private static string BuildMessage(OctoObjectId archiveRtId, StreamDataWriteResult result)
    {
        var manual = result.Skipped.Where(s => s.Origin == ArchiveRowOrigin.Manual).ToList();
        var first = manual.FirstOrDefault();
        return $"{manual.Count} manual row(s) were not stored in archive '{archiveRtId}' because its conflict " +
               "precedence ranks the stored rows above them" +
               (first is null ? "" : $" (first: entity '{first.RtId}' at {first.From:O})") +
               ". Rank the origin first in the archive's ConflictPrecedence ($Origin) so a manual value always wins. " +
               $"{result.Stored} row(s) of the batch were stored.";
    }
}
