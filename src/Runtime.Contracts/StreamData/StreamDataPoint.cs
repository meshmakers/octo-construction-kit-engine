#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Represents a single data point for stream data ingestion.
/// </summary>
public class StreamDataPoint
{
    public required OctoObjectId RtId { get; init; }
    public required RtCkId<CkTypeId> CkTypeId { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? RtWellKnownName { get; init; }
    public DateTime? RtCreationDateTime { get; init; }
    public DateTime? RtChangedDateTime { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// Whether the value was entered manually or delivered automatically. Defaults to
    /// <see cref="ArchiveRowOrigin.Automated" />, so a writer that declares nothing writes an automated row.
    /// </summary>
    public ArchiveRowOrigin Origin { get; init; } = ArchiveRowOrigin.Automated;

    /// <summary>
    /// Who wrote a manual row (AB#7593): set by the platform from the authenticated caller — the asset
    /// repository from the token, a pipeline from its execution identity — never taken from the payload.
    /// Required when <see cref="Origin" /> is <see cref="ArchiveRowOrigin.Manual" />; ignored for automated rows.
    /// </summary>
    public string? ManualWrittenBy { get; init; }

    /// <summary>
    /// Optional free-text reason of a manual write (AB#7593), stored as given. The platform does not
    /// require it; a solution may. Ignored for automated rows.
    /// </summary>
    public string? ManualReason { get; init; }
}
