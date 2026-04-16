#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Contains the result of a stream data query execution.
/// </summary>
public class StreamDataQueryResult
{
    public required IReadOnlyList<StreamDataRow> Rows { get; init; }
    public required long TotalCount { get; init; }
}
