using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     A repository that implements none of the staging copy: every other member throws. It inherits the default
///     implementations of the copy members, so it stands for every existing implementer of
///     <see cref="IStreamDataRepository" /> (AB#5967).
/// </summary>
internal class StreamDataRepositoryStub : IStreamDataRepository
{
    public Task EnsureDatabaseCreatedAsync() => throw new NotSupportedException();
    public Task DeleteDatabaseAsync() => throw new NotSupportedException();
    public Task EnsureArchiveCreatedAsync(ArchiveSnapshot snapshot) => throw new NotSupportedException();
    public Task DeleteArchiveAsync(OctoObjectId archiveRtId) => throw new NotSupportedException();

    public Task ValidateComputedColumnsAsync(OctoObjectId archiveRtId,
        IReadOnlyList<CkArchiveColumnSpec> prospectiveColumns, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string> NormalizeComputedFormulaAsync(OctoObjectId archiveRtId,
        IReadOnlyList<CkArchiveColumnSpec> columns, string formula, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task AddComputedColumnStorageAsync(ArchiveSnapshot snapshot, string columnName,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task AddPendingComputedColumnStorageAsync(ArchiveSnapshot snapshot, string columnName,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task BackfillComputedColumnAsync(ArchiveSnapshot snapshot, string columnName,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task InsertAsync(OctoObjectId archiveRtId, StreamDataPoint datapoint) => throw new NotSupportedException();

    public Task InsertAsync(OctoObjectId archiveRtId, IEnumerable<StreamDataPoint> datapoints) =>
        throw new NotSupportedException();

    public Task InsertTimeRangeAsync(OctoObjectId archiveRtId, IEnumerable<TimeRangeStreamDataPoint> datapoints,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<StreamDataQueryResult> ExecuteQueryAsync(OctoObjectId archiveRtId, StreamDataQueryOptions options) =>
        throw new NotSupportedException();

    public Task<StreamDataQueryResult> ExecuteAggregationQueryAsync(OctoObjectId archiveRtId,
        StreamDataAggregationQueryOptions options) => throw new NotSupportedException();

    public Task<StreamDataQueryResult> ExecuteGroupedAggregationQueryAsync(OctoObjectId archiveRtId,
        StreamDataGroupedAggregationQueryOptions options) => throw new NotSupportedException();

    public Task<StreamDataQueryResult> ExecuteDownsamplingQueryAsync(OctoObjectId archiveRtId,
        StreamDataDownsamplingQueryOptions options) => throw new NotSupportedException();

    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExportRowsAsync(OctoObjectId archiveRtId,
        TimeWindow? window, CancellationToken ct) => throw new NotSupportedException();

    public Task ImportRowsAsync(OctoObjectId archiveRtId, IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        ArchiveImportMode mode, CancellationToken ct) => throw new NotSupportedException();

    public Task<int> AggregateBucketAsync(ArchiveSnapshot sourceArchive, RollupArchiveSnapshot rollup,
        DateTime bucketStart, DateTime bucketEnd, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task ClearRecomputeGenerationsAsync(OctoObjectId rollupRtId, DateTime fromBucketEnd,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<DateTime?> GetArchiveMinTimestampAsync(OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ArchiveCoverage?> GetArchiveCoverageAsync(OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<OctoObjectId, ArchiveStorageStats>> GetArchiveStatsAsync(
        IReadOnlyList<OctoObjectId> archiveRtIds, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
///     The in-memory fake of the staging copy contract (AB#5967): archives are lists of (timestamp, value) rows,
///     the "store" is a dictionary from staging folder to rows. It keeps the order in which slices were copied in
///     <see cref="Events" />, can fail a chosen slice, and mimics the refusals and the bulk-settings handling a
///     real implementation owes the caller. The error message of a failing slice deliberately contains the
///     unredacted location, the way a driver exception does, so the tests can see that the fake scrubs it.
/// </summary>
internal sealed class FakeStagingCopyStreamDataRepository : StreamDataRepositoryStub, IStreamDataRepository
{
    private static readonly string[] ColumnNames = ["timestamp", "value"];

    private readonly Dictionary<OctoObjectId, List<(DateTime Timestamp, long Value)>> _tables = new();
    private readonly Dictionary<string, List<(DateTime Timestamp, long Value)>> _store = new();
    private readonly Dictionary<OctoObjectId, string[]> _columns = new();

    public List<string> Events { get; } = [];
    public bool JobLogFiltered { get; set; } = true;
    public string? FailOnSliceKey { get; set; }
    public bool BulkSettingsActive { get; private set; }
    public bool Optimized { get; private set; }

    public bool SupportsStagingCopy => true;

    public IReadOnlyCollection<string> StoredFolders => _store.Keys.ToList();

    public void AddArchive(OctoObjectId archiveRtId, IEnumerable<(DateTime Timestamp, long Value)> rows,
        string[]? columns = null)
    {
        _tables[archiveRtId] = rows.ToList();
        _columns[archiveRtId] = columns ?? ColumnNames;
    }

    public IReadOnlyList<(DateTime Timestamp, long Value)> Rows(OctoObjectId archiveRtId) =>
        _tables.TryGetValue(archiveRtId, out var rows)
            ? rows.OrderBy(r => r.Timestamp).ToList()
            : [];

    public Task<ArchiveCopyPlan> PlanArchiveCopyAsync(OctoObjectId archiveRtId, ArchiveCopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var slices = Rows(archiveRtId)
            .GroupBy(r => r.Timestamp.Date)
            .OrderBy(g => g.Key)
            .Select(g => ArchiveCopySlice.ForTimeRange(DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                DateTime.SpecifyKind(g.Key.AddDays(1), DateTimeKind.Utc), g.Count()))
            .ToList();
        return Task.FromResult(new ArchiveCopyPlan(archiveRtId, [Describe(archiveRtId)], slices));
    }

    public Task<ArchiveTableDescription> DescribeArchiveTableAsync(OctoObjectId archiveRtId, ArchiveCopyTable table,
        CancellationToken cancellationToken = default) => Task.FromResult(Describe(archiveRtId, table));

    public Task<string> GetStorageVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("5.10.10");

    public async IAsyncEnumerable<ArchiveCopySliceResult> CopyToStagingAsync(OctoObjectId archiveRtId,
        ArchiveCopyPlan plan, ArchiveCopyStagingLocation location, ArchiveCopyOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RefuseWhenTheJobLogIsNotFiltered(archiveRtId);
        await Task.Yield();

        foreach (var slice in plan.Slices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = Folder(location, plan.ArchiveRtId, slice);

            if (slice.Key == FailOnSliceKey)
            {
                Events.Add($"to:{slice.Key}:failed");
                yield return new ArchiveCopySliceResult(slice, ArchiveCopySliceStatus.Failed, 0, 0, TimeSpan.Zero,
                    location.Scrub($"COPY failed for '{location.RevealUri()}': access denied"));
                yield break;
            }

            var rows = Rows(archiveRtId).Where(r => r.Timestamp >= slice.FromUtc && r.Timestamp < slice.ToUtc).ToList();
            _store[folder] = rows;
            Events.Add($"to:{slice.Key}");
            yield return new ArchiveCopySliceResult(slice, ArchiveCopySliceStatus.Succeeded, rows.Count, 0,
                TimeSpan.FromMilliseconds(1),
                rows.Count == slice.RowCount ? null : $"Counted {slice.RowCount} rows, copied {rows.Count}.");
        }
    }

    public async IAsyncEnumerable<ArchiveCopySliceResult> CopyFromStagingAsync(OctoObjectId archiveRtId,
        ArchiveCopyPlan plan, ArchiveCopyStagingLocation location, ArchiveCopyOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RefuseWhenTheJobLogIsNotFiltered(archiveRtId);

        var target = _columns.GetValueOrDefault(archiveRtId) ?? [];
        var dumped = plan.Tables.First(t => t.Table == ArchiveCopyTable.Data).Columns.Select(c => c.Name);
        var missing = dumped.FirstOrDefault(c => !target.Contains(c));
        if (missing is not null)
        {
            throw new ArchiveCopyRefusedException(ArchiveCopyRefusalReason.ColumnMissing,
                $"The dumped column '{missing}' does not exist in the table to load into.", archiveRtId);
        }

        await Task.Yield();
        BulkSettingsActive = true;
        try
        {
            var loaded = _tables.TryGetValue(archiveRtId, out var existing) ? existing : _tables[archiveRtId] = [];
            foreach (var slice in plan.Slices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = Folder(location, plan.ArchiveRtId, slice);

                if (slice.Key == FailOnSliceKey || !_store.TryGetValue(folder, out var rows))
                {
                    Events.Add($"from:{slice.Key}:failed");
                    yield return new ArchiveCopySliceResult(slice, ArchiveCopySliceStatus.Failed, 0, 0,
                        TimeSpan.Zero, location.Scrub($"COPY failed for '{location.RevealUri()}': no such file"));
                    yield break;
                }

                loaded.AddRange(rows);
                Events.Add($"from:{slice.Key}");
                var ok = rows.Count == slice.RowCount;
                yield return new ArchiveCopySliceResult(slice,
                    ok ? ArchiveCopySliceStatus.Succeeded : ArchiveCopySliceStatus.Failed, rows.Count, 0,
                    TimeSpan.FromMilliseconds(1), ok ? null : $"Expected {slice.RowCount} rows, loaded {rows.Count}.");
            }

            Optimized = options?.OptimizeAfterLoad ?? true;
        }
        finally
        {
            BulkSettingsActive = false;
        }
    }

    private void RefuseWhenTheJobLogIsNotFiltered(OctoObjectId archiveRtId)
    {
        if (!JobLogFiltered)
        {
            throw new ArchiveCopyRefusedException(ArchiveCopyRefusalReason.JobLogNotFiltered,
                "stats.jobs_log_filter does not exclude COPY statements.", archiveRtId);
        }
    }

    private ArchiveTableDescription Describe(OctoObjectId archiveRtId, ArchiveCopyTable table = ArchiveCopyTable.Data) =>
        new(archiveRtId, table,
            table == ArchiveCopyTable.Data
                ? (_columns.GetValueOrDefault(archiveRtId) ?? ColumnNames).Select(c =>
                    new ArchiveCopyColumn(c, c == "timestamp" ? "timestamp with time zone" : "bigint")).ToList()
                : []);

    private static string Folder(ArchiveCopyStagingLocation location, OctoObjectId sourceRtId, ArchiveCopySlice slice) =>
        location.WithSubPath(slice.GetStagingPath(sourceRtId)).RedactedUri;
}
