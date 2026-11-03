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

    /// <summary>
    /// Extra columns to group the downsampling result by, in addition to the time bin. Keeps
    /// interleaved series separated (e.g. one source <c>rtId</c> per series) so the result carries
    /// <see cref="StreamDataQueryOptionsBase.Limit"/> bins per series instead of merging every
    /// series into one bin. Null / empty ⇒ single-series grouping by the time bin only.
    /// </summary>
    public IReadOnlyList<string>? GroupByColumnPaths { get; private set; }

    /// <summary>
    /// IANA id of the time zone the bins are cut in (AB#7360), e.g. <c>Europe/Vienna</c>. Null together with
    /// <see cref="BinOrigin"/> and <see cref="BinInterval"/> null keeps the fixed-width UTC bins of earlier
    /// versions. Set (or a <see cref="BinOrigin"/> / <see cref="BinInterval"/> given), the bins are local calendar
    /// days of this zone (UTC when null): 23, 24 or 25 hours across DST changes, built with the same
    /// <c>BucketBoundary</c> logic as day rollups, so raw and rollup bins coincide.
    /// </summary>
    public string? TimeZone { get; private set; }

    /// <summary>
    /// Width of one bin in whole days (AB#7360). Null means one day when <see cref="TimeZone"/> or
    /// <see cref="BinOrigin"/> is set. Must be a whole number of days when a zone or origin is given.
    /// Weeks and months are served by calendar rollups, not here.
    /// </summary>
    public TimeSpan? BinInterval { get; private set; }

    /// <summary>
    /// Local time of day at which a bin starts (AB#7360), e.g. 06:00 for a gas day. A whole number of minutes in
    /// <c>[00:00, 24:00)</c>; null means midnight. Same meaning as <c>DayStartOffsetMs</c> of a day rollup.
    /// </summary>
    public TimeSpan? BinOrigin { get; private set; }

    /// <summary>
    /// True when the bins are local-day bins (a time zone, an origin or an interval is given) instead of the
    /// fixed-width UTC bins derived from range and <see cref="StreamDataQueryOptionsBase.Limit"/>.
    /// </summary>
    public bool HasLocalDayBins => TimeZone is not null || BinInterval is not null || BinOrigin is not null;

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

    public StreamDataDownsamplingQueryOptions WithGroupByColumns(IReadOnlyList<string>? paths)
    {
        GroupByColumnPaths = paths;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithBinInterval(TimeSpan? interval)
    {
        BinInterval = interval;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithTimeZone(string? timeZone)
    {
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone;
        return this;
    }

    public StreamDataDownsamplingQueryOptions WithBinOrigin(TimeSpan? origin)
    {
        BinOrigin = origin;
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
