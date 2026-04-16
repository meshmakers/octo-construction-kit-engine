#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Represents a single row in a stream data query result.
/// </summary>
public class StreamDataRow
{
    public OctoObjectId? RtId { get; init; }
    public RtCkId<CkTypeId>? CkTypeId { get; init; }
    public DateTime? Timestamp { get; init; }
    public string? RtWellKnownName { get; init; }
    public DateTime? RtCreationDateTime { get; init; }
    public DateTime? RtChangedDateTime { get; init; }
    public IReadOnlyDictionary<string, object?> Values { get; init; }
        = new Dictionary<string, object?>();
}
