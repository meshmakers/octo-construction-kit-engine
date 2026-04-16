#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Repository for stream data operations (time-series data stored in CrateDB).
/// Accessed via ITenantContext.GetStreamDataRepository().
/// </summary>
public interface IStreamDataRepository
{
    /// <summary>
    /// Ensures the stream data database/table exists for this tenant.
    /// </summary>
    Task EnsureDatabaseCreatedAsync();

    /// <summary>
    /// Deletes the stream data database/table for this tenant.
    /// </summary>
    Task DeleteDatabaseAsync();

    /// <summary>
    /// Inserts a single data point.
    /// </summary>
    Task InsertAsync(StreamDataPoint datapoint);

    /// <summary>
    /// Inserts multiple data points.
    /// </summary>
    Task InsertAsync(IEnumerable<StreamDataPoint> datapoints);

    /// <summary>
    /// Executes a simple stream data query.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteQueryAsync(StreamDataQueryOptions options);

    /// <summary>
    /// Executes an aggregation query (without grouping).
    /// </summary>
    Task<StreamDataQueryResult> ExecuteAggregationQueryAsync(StreamDataAggregationQueryOptions options);

    /// <summary>
    /// Executes a grouped aggregation query.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteGroupedAggregationQueryAsync(
        StreamDataGroupedAggregationQueryOptions options);

    /// <summary>
    /// Executes a downsampling query with time bins.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteDownsamplingQueryAsync(StreamDataDownsamplingQueryOptions options);
}
