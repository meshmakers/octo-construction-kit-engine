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

    /// <summary>
    /// Validates a prospective computed-column set (AB#4189 Phase 7) — syntax, reference resolution,
    /// acyclic dependencies, Path/Formula exclusivity, nullable, ResultType — without touching
    /// storage. The lifecycle service calls this before persisting an add (existing columns + the
    /// new one) or a remove (existing columns − the removed one, so a now-dangling reference is
    /// rejected). Throws a <c>StreamDataException</c> (stable GraphQL error code) on the first issue.
    /// </summary>
    Task ValidateComputedColumnsAsync(
        OctoObjectId archiveRtId,
        IReadOnlyList<CkArchiveColumnSpec> prospectiveColumns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites a computed-column formula from the archive's <b>logical</b> column vocabulary — the CK
    /// attribute paths the Studio lists and the query surface uses, e.g. <c>Amount.Value</c> — into the
    /// <b>physical</b> form the evaluation path binds, e.g. <c>amountvalue</c> (AB#4779).
    /// <para>
    /// The lifecycle service calls this before validating and persisting, so the stored formula is
    /// always physical and every downstream consumer keeps seeing exactly what it saw before. A name
    /// that is already physical, or that matches no column at all, is left as written — the latter so
    /// validation can reject it by the spelling the caller actually used.
    /// </para>
    /// <para>
    /// It lives on the storage contract rather than in the lifecycle service because the
    /// logical→physical rule belongs to the storage layer, the same reason
    /// <see cref="ValidateComputedColumnsAsync" /> does. No I/O.
    /// </para>
    /// </summary>
    Task<string> NormalizeComputedFormulaAsync(
        OctoObjectId archiveRtId,
        IReadOnlyList<CkArchiveColumnSpec> columns,
        string formula,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the physical CrateDB column for the computed column named <paramref name="columnName"/>
    /// via <c>ALTER TABLE … ADD COLUMN</c> (AB#4189 Phase 7). Idempotent — tolerant of the column
    /// already existing (e.g. a re-add reusing an orphaned column). The column on
    /// <paramref name="snapshot"/> supplies the declared result type the physical column is typed from.
    /// </summary>
    Task AddComputedColumnStorageAsync(
        ArchiveSnapshot snapshot, string columnName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the physical CrateDB column for the <em>pending</em> version of a computed column
    /// (<c>{base}__v{ComputedVersion+1}</c>) via <c>ALTER TABLE … ADD COLUMN</c> (AB#4189 Phase 7,
    /// formula change). Idempotent. Must run before the pending formula is marked so ingest's
    /// dual-write never targets a missing column.
    /// </summary>
    Task AddPendingComputedColumnStorageAsync(
        ArchiveSnapshot snapshot, string columnName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backfills the computed column named <paramref name="columnName"/> across the archive's
    /// existing rows (AB#4189 Phase 7, §8): pages through the rows, evaluates the column's formula
    /// per row, and writes the result into its physical cell. The physical column must already exist
    /// (see <see cref="AddComputedColumnStorageAsync"/>). Readers keep seeing the previous state until
    /// the lifecycle flips the column to <c>Active</c>, because the column is hidden while
    /// non-active. Throws on a storage failure so the lifecycle can mark the column <c>Failed</c>.
    /// </summary>
    Task BackfillComputedColumnAsync(
        ArchiveSnapshot snapshot, string columnName, CancellationToken cancellationToken = default);

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

    // --- Native copy to and from a staging location (AB#5967) ----------------------------------
    // Concept: docs/concept-tenant-backup-native-archive-dump.md. The members have default
    // implementations so that a repository without a staging area (and every existing implementer)
    // stays valid: SupportsStagingCopy is false and the members throw ArchiveCopyNotSupportedException.

    /// <summary>
    /// True when this repository can copy an archive's tables to and from a staging location with the
    /// data store's own bulk statements (CrateDB <c>COPY TO</c> / <c>COPY FROM</c>). When false the
    /// caller takes the row path (<see cref="ExportRowsAsync"/> / <see cref="ImportRowsAsync"/>, dump
    /// format 1). The other members of this section throw <see cref="ArchiveCopyNotSupportedException"/>.
    /// </summary>
    bool SupportsStagingCopy => false;

    /// <summary>
    /// Cuts the archive into the slices a dump copies: time slices of about
    /// <see cref="ArchiveCopyOptions.TargetSliceRows"/> rows, each counted, in ascending time order, and
    /// for a rollup its generation map as the last slice, together with the description of every table.
    /// An archive without a backing table yields a plan without slices. Reads only.
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    Task<ArchiveCopyPlan> PlanArchiveCopyAsync(
        OctoObjectId archiveRtId,
        ArchiveCopyOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException(archiveRtId);

    /// <summary>
    /// Dumps the slices of <paramref name="plan"/> to <paramref name="location"/>, one slice at a time in plan
    /// order, into the folder <see cref="ArchiveCopySlice.GetStagingPath"/> of each slice. Yields the result
    /// of each slice as soon as it is done. The copy ends after the first failed slice (its result says
    /// <see cref="ArchiveCopySliceStatus.Failed"/> with a redacted message; earlier slices stay valid) and
    /// stops between two slices when <paramref name="cancellationToken"/> is cancelled
    /// (<see cref="OperationCanceledException"/>). Refuses to start with
    /// <see cref="ArchiveCopyRefusedException"/> while the data store would record the credential in its job
    /// log.
    /// </summary>
    /// <remarks>
    /// The tenant stays online, the dump is not a snapshot: a slice whose copied rows differ from the counted
    /// ones is reported with a warning in <see cref="ArchiveCopySliceResult.Message"/>, not as a failure.
    /// <paramref name="plan"/> must have been made for <paramref name="archiveRtId"/>. No text that leaves
    /// this method — result message, exception, log, trace — contains the credential of
    /// <paramref name="location"/>.
    /// </remarks>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    IAsyncEnumerable<ArchiveCopySliceResult> CopyToStagingAsync(
        OctoObjectId archiveRtId,
        ArchiveCopyPlan plan,
        ArchiveCopyStagingLocation location,
        ArchiveCopyOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException(archiveRtId);

    /// <summary>
    /// Loads the slices of <paramref name="plan"/> from <paramref name="location"/> into the table of
    /// <paramref name="archiveRtId"/>, which the caller created fresh with the platform's DDL. The files are
    /// read from <see cref="ArchiveCopySlice.GetStagingPath"/> of the <b>plan's</b> archive id, so a restore
    /// into another archive id reads the source's files. Yields the result of each slice as soon as it is
    /// done; a slice counts as loaded when the loaded rows equal <see cref="ArchiveCopySlice.RowCount"/> and
    /// no row was rejected. The copy ends after the first failed slice and stops between two slices on
    /// cancellation.
    /// </summary>
    /// <remarks>
    /// Before the first slice it refuses with <see cref="ArchiveCopyRefusedException"/> when a dumped column
    /// is missing in the target table (message names the column) or a node lacks the free disk space the load
    /// needs (message names expected and available size), and when the data store would record the credential
    /// in its job log. The bulk table settings it sets for the load (no replicas, no refresh) are restored
    /// when the enumeration ends — also after a failed slice, an exception or cancellation. After the last
    /// slice it merges the segments when <see cref="ArchiveCopyOptions.OptimizeAfterLoad"/> is set. A
    /// different storage version on source and target is logged, not refused.
    /// A NULL in a column that has a DEFAULT is not preserved (the default applies); see the concept §7.
    /// </remarks>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    IAsyncEnumerable<ArchiveCopySliceResult> CopyFromStagingAsync(
        OctoObjectId archiveRtId,
        ArchiveCopyPlan plan,
        ArchiveCopyStagingLocation location,
        ArchiveCopyOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException(archiveRtId);

    /// <summary>
    /// Describes one physical table of the archive: every column with its backend data type, as the dump
    /// manifest records it. A table that does not exist (archive not activated, no generation map) yields an
    /// empty column list; every other storage failure propagates.
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    Task<ArchiveTableDescription> DescribeArchiveTableAsync(
        OctoObjectId archiveRtId,
        ArchiveCopyTable table,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException(archiveRtId);

    /// <summary>
    /// The version of the data store (CrateDB: <c>sys.nodes</c> version) the dump manifest records and a
    /// restore logs next to the target's.
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    Task<string> GetStorageVersionAsync(CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

    // --- Archive-level dump and load, rollup consistency (AB#5967 slice 2) ---------------------------
    // Default implementations as above: a repository without the native copy throws
    // ArchiveCopyNotSupportedException.

    /// <summary>
    /// Dumps one archive completely: plans it, copies its data slices and — for a rollup — its generation map, and applies the
    /// repeat rule. For a rollup the map is read first, copied, then the data slices are copied, then the map is read again; when it
    /// moved in between (a recompute flipped a pointer) the whole rollup is copied again, up to
    /// <see cref="ArchiveCopyOptions.MaxMapAttempts"/> times. The result states the attempts and the times of the map read and of
    /// the last data slice. An archive without a generation map is copied once and its map is never read.
    /// </summary>
    /// <exception cref="ArchiveCopyRefusedException">
    /// <see cref="ArchiveCopyRefusalReason.GenerationMapKeptMoving"/>: the map moved in every attempt (the message names the rollup),
    /// or any reason of <see cref="CopyToStagingAsync"/>.
    /// </exception>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    Task<ArchiveCopyDumpResult> DumpArchiveToStagingAsync(
        OctoObjectId archiveRtId,
        ArchiveCopyStagingLocation location,
        ArchiveCopyOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException(archiveRtId);

    /// <summary>
    /// Loads one archive from <paramref name="plan"/> (data slices first, the generation map last, the previous version of the rollup
    /// is not involved) and states whether the tables hold exactly the counted rows (<see cref="ArchiveCopyLoadResult.RowCountVerified"/>).
    /// Same checks, bulk settings and refusals as <see cref="CopyFromStagingAsync"/>.
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository has no staging copy.</exception>
    Task<ArchiveCopyLoadResult> LoadArchiveFromStagingAsync(
        OctoObjectId archiveRtId,
        ArchiveCopyPlan plan,
        ArchiveCopyStagingLocation location,
        ArchiveCopyOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException(archiveRtId);

    /// <summary>
    /// Runs the deferred sweeps of recomputed rollup ranges that are due. A recompute flips a range to a new generation and, with
    /// a sweep grace period above zero, records the sweep of the superseded generation durably instead of running it at once (a
    /// native dump that read the generation map before the flip still finds the rows its copy names); this call executes every
    /// record whose flip time plus grace period has passed, so it also works after a restart. Returns the number of recorded
    /// sweeps that were executed or dropped as obsolete. A repository that defers nothing returns 0.
    /// </summary>
    /// <remarks>Called by the host's recompute tick for every tenant, not while the tenant is marked <em>restore reconcile pending</em>.</remarks>
    Task<int> SweepSupersededGenerationsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    /// <summary>
    /// Checks rollups for the inconsistencies a restore (or a dump taken during a recompute) can leave: a pointer without rows, rows
    /// above the pointer, a watermark ahead of the rows, a missing generation map (<see cref="RollupConsistencyCheck"/>). Read only:
    /// counts and MAX/MIN statements, no row is exported, nothing is changed. Does not throw for an inconsistent rollup; the report
    /// lists it.
    /// </summary>
    /// <param name="rollupRtIds">The rollups to check; <c>null</c> checks every rollup archive of the tenant.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository does not support the check.</exception>
    Task<RollupConsistencyReport> ValidateRollupsAsync(
        IReadOnlyCollection<OctoObjectId>? rollupRtIds = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

    /// <summary>
    /// Validates, repairs what the validator found, and validates again (<see cref="RollupRepairReport"/>). Idempotent: a second run on
    /// a repaired tenant finds nothing to do. With <paramref name="dryRun"/> it lists the planned steps and changes nothing. A failing
    /// step stops the repair of its rollup and is named in the report; the restore-reconcile marker is cleared only when the repair
    /// ended with a tenant that validates ok (<see cref="IsRestoreReconcilePendingAsync"/>).
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository does not support the repair.</exception>
    Task<RollupRepairReport> RepairRollupsAsync(
        IReadOnlyCollection<OctoObjectId>? rollupRtIds = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

    /// <summary>
    /// Queues a repair (<see cref="RepairRollupsAsync"/>) instead of running it under the caller: the plan is recorded durably
    /// (the request returns at once with the planned steps) and the host's recompute tick runs it, also after a restart
    /// (<see cref="RunPendingRollupRepairsAsync"/>). The repair clears the restore reconcile marker when its final validation
    /// is ok; if a step fails the request is <see cref="RollupRepairRequestState.Failed"/>, its report names the step, and the
    /// marker stays. For the REST, CLI and MCP surfaces, where an inline repair of a large tenant would outlive the request.
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository does not support the repair.</exception>
    Task<RollupRepairRequest> EnqueueRollupRepairAsync(
        IReadOnlyCollection<OctoObjectId>? rollupRtIds = null,
        CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

    /// <summary>The state and, once it ended, the report of a queued repair; <c>null</c> for an unknown id.</summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository does not support the repair.</exception>
    Task<RollupRepairRequest?> GetRollupRepairRequestAsync(string requestId, CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

    /// <summary>
    /// Runs the queued repairs of the tenant that are pending (or were left running by a process that died). Returns how many
    /// were run. Called by the host's recompute tick, which does not hold a marked tenant back from this call.
    /// </summary>
    Task<int> RunPendingRollupRepairsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    /// <summary>
    /// True while the tenant is marked <em>restore reconcile pending</em>: a restore with archive data finished and the rollups were not
    /// yet validated ok or repaired. While it is set, reclaim, forward aggregation and recompute do not run for the tenant. Absent
    /// marker = not pending, so tenants and backups from before this feature work unchanged.
    /// </summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository does not support the marker.</exception>
    Task<bool> IsRestoreReconcilePendingAsync(CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

    /// <summary>Sets or clears the <em>restore reconcile pending</em> marker (stored durably next to the rollup data).</summary>
    /// <exception cref="ArchiveCopyNotSupportedException">The repository does not support the marker.</exception>
    Task SetRestoreReconcilePendingAsync(bool pending, CancellationToken cancellationToken = default)
        => throw new ArchiveCopyNotSupportedException();

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
    /// <para>
    /// Single-source per call, unchanged by AB#5157: a multi-source rollup aggregates one bucket
    /// from exactly one source, and the orchestrator picks that source per bucket via
    /// <see cref="RollupArchiveSnapshot.SourceForBucket"/> before calling in. A bucket no source
    /// span covers is never passed here at all — the orchestrator writes no row and advances the
    /// watermark instead.
    /// </para>
    /// </remarks>
    Task<int> AggregateBucketAsync(
        ArchiveSnapshot sourceArchive,
        RollupArchiveSnapshot rollup,
        DateTime bucketStart,
        DateTime bucketEnd,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears the per-window recompute generation pointers for a rollup archive at and after
    /// <paramref name="fromBucketEnd"/>, so a subsequent forward re-aggregation (which writes
    /// generation 0) becomes visible again. Called when a rollup watermark is rewound over a range
    /// that was previously recomputed (AB#4184, Phase 6): the rewind re-aggregates forward at
    /// generation 0, but the active-generation pointer would otherwise keep readers on the stale
    /// recomputed generation. Implementations remove the generation-map entries whose range reaches
    /// past <paramref name="fromBucketEnd"/> and the superseded (generation &gt; 0) rows in that
    /// range. Idempotent — a no-op when there is no recompute state (no genmap / non-rollup).
    /// </summary>
    Task ClearRecomputeGenerationsAsync(
        OctoObjectId rollupRtId,
        DateTime fromBucketEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the earliest stored timestamp in the archive's backing table — <c>MIN(window_start)</c>
    /// for windowed (rollup / time-range) archives, <c>MIN(timestamp)</c> for raw archives (AB#4269).
    /// Used by rollup backfill to recompute a rollup over its source archive's entire history without
    /// the operator supplying a start timestamp. Returns <c>null</c> when the archive has no backing
    /// table yet (e.g. <c>Created</c>) or the table is empty.
    /// </summary>
    Task<DateTime?> GetArchiveMinTimestampAsync(
        OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Measures the archive's stored time range (AB#5157): <c>MIN(window_start)</c> /
    /// <c>MAX(window_end)</c> for windowed (rollup / time-range) archives and
    /// <c>MIN(timestamp)</c> / <c>MAX(timestamp)</c> for raw archives. Returns <c>null</c> when the
    /// archive holds no data — no backing table yet (e.g. <c>Created</c>), an unknown relation, or
    /// an empty table — never a sentinel range. Every other storage failure propagates.
    /// </summary>
    /// <remarks>
    /// Coverage is archive-wide (not per entity rtId) and a single from/to pair; gaps inside the
    /// range are not represented. Callers normally go through <see cref="IArchiveCoverageProvider"/>
    /// so the answer is memoised per tenant instead of probed on every request.
    /// </remarks>
    Task<ArchiveCoverage?> GetArchiveCoverageAsync(
        OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default);

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
