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
/// AB#5189 item B: the recompute never claims a bucket the forward aggregation will write
/// <em>again</em>. Both orchestrators are driven off the same clock and the same rollup definition,
/// and the buckets each one actually writes are compared.
/// </summary>
/// <remarks>
/// <para>
/// The forward path closes a bucket only once <c>bucketEnd &lt;= now - WatermarkLag</c> and keeps
/// re-writing the one before that as the provisional open bucket (AB#4306). Before the fix the
/// recompute capped its range at the start of the bucket containing <c>now</c> — with no lag — so
/// the buckets inside the lag window belonged to both. The recompute's per-window generation
/// pointer wins the read path, so the forward pass's later, complete value for that bucket was
/// masked: the late data the lag exists to absorb never surfaced.
/// </para>
/// <para>
/// The precise guarantee is NOT "lag-closed buckets are written only by the recompute": the forward
/// pass writes a lag-closed bucket exactly once, when it closes it, and with a backlog watermark that
/// happens on the same tick a recompute covers it. Both values are final, so either order is
/// correct. What must never happen is a recompute write into a bucket the forward pass will
/// <em>re</em>-write — the provisional one inside the lag window. The two watermarks below pin both
/// halves: the one that lets the overlap on a lag-closed bucket occur, and the one that does not.
/// </para>
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
    private static readonly DateTime Frontier = Now - WatermarkLag;

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

        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(Source());
    }

    private void StubRollup(DateTime watermark)
    {
        var rollup = new RollupArchiveSnapshot(RollupRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(SourceRt) },
            BucketSize, WatermarkLag, watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }, null);
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(rollup);
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(new[] { rollup }));
    }

    private static ArchiveSnapshot Source() =>
        new(SourceRt, SourceType, CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    // 13: the watermark already sits at the frontier bucket — the forward pass closes nothing and
    //     only refreshes [13:00, 14:00) provisionally.
    // 12: a one-bucket backlog — the forward pass closes [12:00, 13:00) on this very tick AND
    //     refreshes [13:00, 14:00); the recompute covers [12:00, 13:00) too.
    [Theory]
    [InlineData(13)]
    [InlineData(12)]
    public async Task Recompute_NeverClaimsABucketTheForwardPassWillWriteAgain(int watermarkHour)
    {
        StubRollup(new DateTime(2026, 5, 11, watermarkHour, 0, 0, DateTimeKind.Utc));

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

        // --- 1. every bucket the recompute claims is lag-closed: the forward pass writes it at most
        //        once more (when closing it) and never again after that ----------------------------
        Assert.All(recomputeRanges, r => Assert.True(
            r.End <= Frontier,
            $"Recompute claimed [{r.Start:O},{r.End:O}), which reaches past the lag frontier {Frontier:O} " +
            $"(now={Now:O}, lag {WatermarkLag}) into buckets the forward pass will still re-write."));

        // --- 2. the forward pass's provisional buckets — the ones it WILL write again — are disjoint
        //        from everything the recompute wrote ------------------------------------------------
        var provisional = forwardBuckets.Where(b => b.End > Frontier).ToList();
        Assert.NotEmpty(provisional);
        var contested = provisional
            .Where(b => recomputeRanges.Any(r => b.Start < r.End && r.Start < b.End))
            .ToList();
        Assert.True(
            contested.Count == 0,
            $"Provisional bucket(s) written by BOTH the forward aggregation and the recompute: " +
            $"{string.Join(", ", contested.Select(b => $"[{b.Start:O},{b.End:O})"))}. " +
            $"Recompute claimed {string.Join(", ", recomputeRanges.Select(r => $"[{r.Start:O},{r.End:O})"))} " +
            $"with now={Now:O} and watermark lag {WatermarkLag}.");

        // --- 3. with a backlog the forward pass closes a lag-closed bucket the recompute also covers.
        //        That overlap is allowed — both values are final — and this is where a stricter
        //        "never the same bucket" assertion would fail for a perfectly correct system --------
        var closedThisTick = forwardBuckets.Where(b => b.End <= Frontier).ToList();
        if (watermarkHour == 12)
        {
            Assert.Contains(closedThisTick, b => recomputeRanges.Any(r => b.Start < r.End && r.Start < b.End));
        }
        else
        {
            Assert.Empty(closedThisTick);
        }
    }
}
