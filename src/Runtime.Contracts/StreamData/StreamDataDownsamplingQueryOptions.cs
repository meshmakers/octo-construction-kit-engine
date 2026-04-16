#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Options for a downsampling stream data query with time bins.
/// </summary>
public class StreamDataDownsamplingQueryOptions : StreamDataQueryOptionsBase
{
    public IReadOnlyList<AggregationColumn> AggregationColumns { get; private set; }
        = Array.Empty<AggregationColumn>();

    public TimeSpan BinInterval { get; private set; }

    public static StreamDataDownsamplingQueryOptions Create() => new();

    public StreamDataDownsamplingQueryOptions WithCkTypeId(RtCkId<CkTypeId> id)
    {
        CkTypeId = id;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithAggregationColumns(
        IReadOnlyList<AggregationColumn> columns)
    {
        AggregationColumns = columns;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithBinInterval(TimeSpan interval)
    {
        BinInterval = interval;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithTimeRange(DateTime? from, DateTime? to)
    {
        From = from;
        To = to;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithLimit(int? limit)
    {
        Limit = limit;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithFieldFilters(
        IReadOnlyList<FieldFilter>? fieldFilters)
    {
        FieldFilters = fieldFilters;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithRtIds(IReadOnlyList<OctoObjectId>? ids)
    {
        RtIds = ids;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithPagination(int? offset, int? pageSize)
    {
        Offset = offset;
        PageSize = pageSize;
        return this;
    }
}
