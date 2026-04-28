#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Repository for stream data operations against a tenant-scoped, CrateDB-backed time-series store
/// organised by <c>CkArchive</c> instances. Accessed via
/// <c>ITenantContext.GetStreamDataRepository()</c>. Every operation is scoped to a single archive,
/// addressed by its <see cref="OctoObjectId"/> runtime id.
/// </summary>
public interface IStreamDataRepository
{
    // --- Tenant-level control plane -----------------------------------------------------------

    /// <summary>
    /// Ensures the tenant's stream data namespace (e.g. CrateDB schema) exists. Idempotent.
    /// Called when a tenant opts into stream data; per-archive tables are created later via
    /// <see cref="EnsureArchiveCreatedAsync"/>.
    /// </summary>
    Task EnsureDatabaseCreatedAsync();

    /// <summary>
    /// Drops the tenant's entire stream data namespace including every archive table. Idempotent.
    /// </summary>
    Task DeleteDatabaseAsync();

    // --- Per-archive control plane ------------------------------------------------------------

    /// <summary>
    /// Creates the storage table for the archive identified by <paramref name="archiveRtId"/>
    /// according to its current <c>CkArchive</c> definition. Idempotent (uses
    /// <c>CREATE TABLE IF NOT EXISTS</c>) so retries after a transient Mongo update failure
    /// converge cleanly.
    /// </summary>
    Task EnsureArchiveCreatedAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Drops the storage table for the archive identified by <paramref name="archiveRtId"/>.
    /// Idempotent. Called from the lifecycle service when an archive is deleted.
    /// </summary>
    Task DeleteArchiveAsync(OctoObjectId archiveRtId);

    // --- Data plane ---------------------------------------------------------------------------

    /// <summary>
    /// Inserts a single data point into the archive. Throws
    /// <c>ArchiveNotActivatedException</c> if the archive's status is not <c>Activated</c>;
    /// throws <c>RequiredAttributeMissingException</c> on a missing required path.
    /// </summary>
    Task InsertAsync(OctoObjectId archiveRtId, StreamDataPoint datapoint);

    /// <summary>
    /// Inserts multiple data points into the archive. Pre-validates the entire batch before any
    /// SQL is sent; on first violation no row is written and the offending point's index is
    /// surfaced via the thrown exception.
    /// </summary>
    Task InsertAsync(OctoObjectId archiveRtId, IEnumerable<StreamDataPoint> datapoints);

    /// <summary>
    /// Executes a simple stream data query against the archive.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteQueryAsync(
        OctoObjectId archiveRtId, StreamDataQueryOptions options);

    /// <summary>
    /// Executes an aggregation query (without grouping) against the archive.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteAggregationQueryAsync(
        OctoObjectId archiveRtId, StreamDataAggregationQueryOptions options);

    /// <summary>
    /// Executes a grouped aggregation query against the archive.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteGroupedAggregationQueryAsync(
        OctoObjectId archiveRtId, StreamDataGroupedAggregationQueryOptions options);

    /// <summary>
    /// Executes a downsampling query with time bins against the archive.
    /// </summary>
    Task<StreamDataQueryResult> ExecuteDownsamplingQueryAsync(
        OctoObjectId archiveRtId, StreamDataDownsamplingQueryOptions options);
}
