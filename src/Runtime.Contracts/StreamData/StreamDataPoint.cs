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
}
