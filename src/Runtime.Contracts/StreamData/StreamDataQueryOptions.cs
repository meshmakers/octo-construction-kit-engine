#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Base class for all stream data query options.
/// </summary>
public abstract class StreamDataQueryOptionsBase
{
    public RtCkId<CkTypeId> CkTypeId { get; protected set; } = null!;
    public IReadOnlyList<string> Columns { get; protected set; } = Array.Empty<string>();
    public IReadOnlyList<OctoObjectId>? RtIds { get; protected set; }
    public DateTime? From { get; protected set; }
    public DateTime? To { get; protected set; }
    public int? Limit { get; protected set; }
    public IReadOnlyList<SortOrderItem>? SortOrders { get; protected set; }
    public IReadOnlyList<FieldFilter>? FieldFilters { get; protected set; }
    public int? Offset { get; protected set; }
    public int? PageSize { get; protected set; }
}

/// <summary>
/// Options for a simple stream data query. Use <see cref="Create"/> to start building.
/// </summary>
public class StreamDataQueryOptions : StreamDataQueryOptionsBase
{
    public static StreamDataQueryOptions Create() => new();

    public StreamDataQueryOptions WithCkTypeId(RtCkId<CkTypeId> id)
    {
        CkTypeId = id;
        return this;
    }

    public StreamDataQueryOptions WithColumns(IReadOnlyList<string> columns)
    {
        Columns = columns;
        return this;
    }

    public StreamDataQueryOptions WithRtIds(IReadOnlyList<OctoObjectId>? ids)
    {
        RtIds = ids;
        return this;
    }

    public StreamDataQueryOptions WithTimeRange(DateTime? from, DateTime? to)
    {
        From = from;
        To = to;
        return this;
    }

    public StreamDataQueryOptions WithLimit(int? limit)
    {
        Limit = limit;
        return this;
    }

    public StreamDataQueryOptions WithSortOrders(IReadOnlyList<SortOrderItem>? sortOrders)
    {
        SortOrders = sortOrders;
        return this;
    }

    public StreamDataQueryOptions WithFieldFilters(IReadOnlyList<FieldFilter>? fieldFilters)
    {
        FieldFilters = fieldFilters;
        return this;
    }

    public StreamDataQueryOptions WithPagination(int? offset, int? pageSize)
    {
        Offset = offset;
        PageSize = pageSize;
        return this;
    }
}
