#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using System.Threading;
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
    /// Creates the storage table for the archive described by <paramref name="snapshot"/> according
    /// to its current <c>CkArchive</c> definition. The snapshot carries the target CK type and
    /// user-picked columns the data store needs to generate DDL — passing it directly avoids a
    /// round-trip through <see cref="IArchiveRuntimeStore"/> from inside the repository.
    /// Idempotent (uses <c>CREATE TABLE IF NOT EXISTS</c>) so retries after a transient Mongo
    /// update failure converge cleanly.
    /// </summary>
    Task EnsureArchiveCreatedAsync(ArchiveSnapshot snapshot);

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
    /// Inserts externally pre-aggregated time-range data points into a <c>TimeRangeArchive</c>.
    /// Each point carries an explicit <c>[from, to)</c> window; the natural key
    /// <c>(window_start, window_end, rtid, ckTypeId)</c> handles re-deliveries via
    /// <c>ON CONFLICT DO UPDATE</c>, setting the row's <c>was_updated</c> flag to true on every
    /// upsert. Concept §3 / §5. Throws <c>ArchiveNotActivatedException</c> if the archive is not
    /// in Activated state, and <c>ArgumentException</c> if any point has <c>To &lt;= From</c>.
    /// </summary>
    Task InsertTimeRangeAsync(
        OctoObjectId archiveRtId,
        IEnumerable<TimeRangeStreamDataPoint> datapoints,
        CancellationToken cancellationToken = default);

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

    // --- Bulk data export / import (AB#4230) --------------------------------------------------

    /// <summary>
    /// Streams the rows of the archive in a stable key order (keyset pagination on the natural
    /// key) so the caller can serialise NDJSON without buffering the table. When
    /// <paramref name="window"/> is non-null only rows whose <c>timestamp</c> (raw) or
    /// <c>window_start</c> (windowed: rollup / time-range) fall in <c>[FromUtc, ToUtc)</c> are
    /// emitted — the predicate rides on the already time-ordered keyset scan, so a windowed export
    /// is no more expensive than a full one (and cheaper). Each row is yielded as a dictionary of
    /// physical CrateDB column name → value (the standard columns plus the user columns). An archive
    /// without a backing table (e.g. <c>Created</c>) yields no rows rather than throwing.
    /// Archive data export/import concept (AB#4230) §4.1.
    /// </summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExportRowsAsync(
        OctoObjectId archiveRtId, TimeWindow? window, CancellationToken ct);

    /// <summary>
    /// Bulk-inserts pre-parsed archive rows (the inverse of <see cref="ExportRowsAsync"/>). Rows are
    /// streamed in batches into the existing batched insert path (the single-timestamp insert for
    /// raw archives, the <c>(window_start, window_end)</c> ON CONFLICT path for windowed archives).
    /// <paramref name="mode"/> selects insert-only versus upsert semantics on the natural key. Each
    /// row's <c>rtid</c> is validated as 24-char hex; a violation surfaces a per-field error rather
    /// than a generic message. Archive data export/import concept (AB#4230) §4.1 / §7 / §10.
    /// </summary>
    Task ImportRowsAsync(
        OctoObjectId archiveRtId,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        ArchiveImportMode mode,
        CancellationToken ct);

    // --- Rollup data plane --------------------------------------------------------------------

    /// <summary>
    /// Aggregates one bucket from <paramref name="sourceArchive"/> into <paramref name="rollup"/>:
    /// reads source rows with <c>timestamp ∈ [bucketStart, bucketEnd)</c>, groups by <c>rtId</c>,
    /// applies the <see cref="CkRollupAggregationSpec"/> aggregations, and upserts one row per
    /// entity into the rollup archive's table with <c>timestamp = bucketEnd</c>. Rollup-archives
    /// concept §5.
    /// </summary>
    /// <remarks>
    /// Idempotent on the natural key <c>(timestamp, rtId)</c>: when the same bucket is
    /// re-aggregated (e.g. after a watermark rewind, or a crash before
    /// <see cref="IRollupArchiveRuntimeStore.AdvanceWatermarkAsync"/> committed), the
    /// implementation must collapse duplicates via the data store's upsert primitive
    /// (CrateDB: <c>ON CONFLICT (timestamp, rtId) DO UPDATE</c>) so the orchestrator can
    /// always retry safely. Returns the number of upserted target rows.
    /// </remarks>
    Task<int> AggregateBucketAsync(
        ArchiveSnapshot sourceArchive,
        RollupArchiveSnapshot rollup,
        DateTime bucketStart,
        DateTime bucketEnd,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns per-archive storage stats (row count, on-disk size, health) for each
    /// <paramref name="archiveRtIds"/> entry. Bulk call so the studio's archives list can render
    /// stats columns without an N+1 round-trip per row. Archives whose backing table doesn't
    /// exist yet (not activated, or post-delete) appear in the result with
    /// <see cref="ArchiveStorageStats.TableExists"/> false and zero counters — the caller is not
    /// expected to filter the input list beforehand.
    /// </summary>
    /// <remarks>
    /// Implementations may issue a single underlying query against their introspection surface
    /// (e.g. CrateDB <c>sys.shards</c> + <c>sys.health</c>) and return one entry per requested
    /// rtId. Order of returned entries is implementation-defined; callers must look up by rtId.
    /// </remarks>
    Task<IReadOnlyDictionary<OctoObjectId, ArchiveStorageStats>> GetArchiveStatsAsync(
        IReadOnlyList<OctoObjectId> archiveRtIds,
        CancellationToken cancellationToken = default);
}
