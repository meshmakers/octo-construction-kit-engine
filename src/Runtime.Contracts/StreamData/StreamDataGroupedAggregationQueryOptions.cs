#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Options for a grouped aggregation stream data query.
/// </summary>
public class StreamDataGroupedAggregationQueryOptions : StreamDataQueryOptionsBase
{
    public IReadOnlyList<AggregationColumn> AggregationColumns { get; private set; }
        = Array.Empty<AggregationColumn>();

    public IReadOnlyList<string> GroupByColumns { get; private set; }
        = Array.Empty<string>();

    public static StreamDataGroupedAggregationQueryOptions Create() => new();

    public StreamDataGroupedAggregationQueryOptions WithCkTypeId(RtCkId<CkTypeId> id)
    {
        CkTypeId = id;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithAggregationColumns(
        IReadOnlyList<AggregationColumn> columns)
    {
        AggregationColumns = columns;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithGroupByColumns(
        IReadOnlyList<string> columns)
    {
        GroupByColumns = columns;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithTimeRange(DateTime? from, DateTime? to)
    {
        From = from;
        To = to;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithLimit(int? limit)
    {
        Limit = limit;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithFieldFilters(
        IReadOnlyList<FieldFilter>? fieldFilters)
    {
        FieldFilters = fieldFilters;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithRtIds(IReadOnlyList<OctoObjectId>? ids)
    {
        RtIds = ids;
        return this;
    }

    public StreamDataGroupedAggregationQueryOptions WithPagination(int? offset, int? pageSize)
    {
        Offset = offset;
        PageSize = pageSize;
        return this;
    }
}
