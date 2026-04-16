#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Options for a stream data aggregation query (without grouping).
/// </summary>
public class StreamDataAggregationQueryOptions : StreamDataQueryOptionsBase
{
    public IReadOnlyList<AggregationColumn> AggregationColumns { get; private set; }
        = Array.Empty<AggregationColumn>();

    public static StreamDataAggregationQueryOptions Create() => new();

    public StreamDataAggregationQueryOptions WithCkTypeId(RtCkId<CkTypeId> id)
    {
        CkTypeId = id;
        return this;
    }

    public StreamDataAggregationQueryOptions WithAggregationColumns(
        IReadOnlyList<AggregationColumn> columns)
    {
        AggregationColumns = columns;
        return this;
    }

    public StreamDataAggregationQueryOptions WithTimeRange(DateTime? from, DateTime? to)
    {
        From = from;
        To = to;
        return this;
    }

    public StreamDataAggregationQueryOptions WithLimit(int? limit)
    {
        Limit = limit;
        return this;
    }

    public StreamDataAggregationQueryOptions WithFieldFilters(
        IReadOnlyList<FieldFilter>? fieldFilters)
    {
        FieldFilters = fieldFilters;
        return this;
    }

    public StreamDataAggregationQueryOptions WithRtIds(IReadOnlyList<OctoObjectId>? ids)
    {
        RtIds = ids;
        return this;
    }

    public StreamDataAggregationQueryOptions WithPagination(int? offset, int? pageSize)
    {
        Offset = offset;
        PageSize = pageSize;
        return this;
    }
}
