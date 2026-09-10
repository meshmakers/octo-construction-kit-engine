using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5189 item B: exactly one of the forward aggregation and the recompute may own a given bucket.
/// Both orchestrators are driven off the same clock and the same rollup definition, and the buckets
/// each one actually writes are compared — the assertion is on the overlap itself, so it holds
/// whichever side is made to yield.
/// </summary>
/// <remarks>
/// The forward path closes a bucket only once <c>bucketEnd &lt;= now - WatermarkLag</c> and keeps
/// re-writing the one before that as the provisional open bucket (AB#4306). The recompute caps its
/// range at the start of the bucket containing <c>now</c> — with no lag — so the buckets inside the
/// lag window belong to both. The recompute's per-window generation pointer wins the read path, so
/// the forward pass's later, complete value for that bucket is masked: the late data the lag exists
/// to absorb never surfaces.
/// </remarks>
public class RecomputeForwardBucketOwnershipTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId RollupRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId JobRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly RtCkId<CkTypeId> SourceType = new("Test", new CkTypeId("TempSensor"));

    private static readonly TimeSpan BucketSize = TimeSpan.FromHours(1);
    private static readonly TimeSpan WatermarkLag = TimeSpan.FromMinutes(5);

    // 14:02 with a 5-minute lag: the bucket [13:00, 14:00) has ended in wall-clock terms but is not
    // yet lag-closed, so the forward pass still treats it as the provisional open bucket.
    private static readonly DateTime Now = new(2026, 5, 11, 14, 2, 0, DateTimeKind.Utc);
    private static readonly DateTime Watermark = new(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly IStreamDataRepository _streamData = A.Fake<IStreamDataRepository>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();
    private readonly IRollupDependencyGraph _graph = A.Fake<IRollupDependencyGraph>();
    private readonly IArchiveRecomputeStateStore _stateStore = A.Fake<IArchiveRecomputeStateStore>();
    private readonly IRecomputeJobStore _jobStore = A.Fake<IRecomputeJobStore>();
    private readonly IArchiveRecomputeExecutor _executor = A.Fake<IArchiveRecomputeExecutor>();

    public RecomputeForwardBucketOwnershipTests()
    {
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).Returns(JobRt);
        A.CallTo(() => _jobStore.GetActiveForArchiveAsync(A<OctoObjectId>._)).Returns((RecomputeJobSnapshot?)null);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(A<OctoObjectId>._))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)Array.Empty<RollupArchiveSnapshot>());
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .Returns(new RecomputeExecutionResult(1, 1));

        var rollup = Rollup();
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(rollup);
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(new[] { rollup }));
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(Source());
    }

    private static RollupArchiveSnapshot Rollup() =>
        new(RollupRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(SourceRt) },
            BucketSize, WatermarkLag, Watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }, null);

    private static ArchiveSnapshot Source() =>
        new(SourceRt, SourceType, CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    [Fact]
    public async Task ForwardTickAndRecompute_NeverWriteTheSameBucket()
    {
        // --- what the forward pass writes on this tick -----------------------------------------
        var forwardBuckets = new List<(DateTime Start, DateTime End)>();
        A.CallTo(() => _streamData.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, RollupArchiveSnapshot _, DateTime start, DateTime end, CancellationToken _) =>
                forwardBuckets.Add((start, end)))
            .Returns(1);

        var forward = new RollupOrchestrator(
            TenantId, _archiveStore, _rollupStore, _streamData, _audit,
            NullLogger<RollupOrchestrator>.Instance, 60, () => Now, refreshOpenBucket: true);
        await forward.TickAsync(CancellationToken.None);

        // --- what a recompute reaching up to "now" claims ---------------------------------------
        var recomputeRanges = new List<(DateTime Start, DateTime End)>();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, RollupArchiveSnapshot _, DateTime start, DateTime end,
                    OctoObjectId? _, CancellationToken _) =>
                recomputeRanges.Add((start, end)))
            .Returns(new RecomputeExecutionResult(1, 1));

        var recompute = new RecomputeOrchestrator(
            TenantId, _archiveStore, _rollupStore, _graph, _stateStore, _jobStore, _executor, _streamData,
            _audit, NullLogger<RecomputeOrchestrator>.Instance, () => Now);

        var job = await recompute.RecomputeArchiveAsync(
            RollupRt,
            new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc),
            Now,
            null,
            RecomputeTrigger.Manual,
            CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        Assert.NotEmpty(forwardBuckets);
        Assert.NotEmpty(recomputeRanges);

        // --- the two must not claim the same bucket ---------------------------------------------
        var contested = forwardBuckets
            .Where(b => recomputeRanges.Any(r => b.Start < r.End && r.Start < b.End))
            .ToList();

        Assert.True(
            contested.Count == 0,
            $"Bucket(s) written by BOTH the forward aggregation and the recompute: " +
            $"{string.Join(", ", contested.Select(b => $"[{b.Start:O},{b.End:O})"))}. " +
            $"Recompute claimed {string.Join(", ", recomputeRanges.Select(r => $"[{r.Start:O},{r.End:O})"))} " +
            $"with now={Now:O} and watermark lag {WatermarkLag}.");
    }
}
