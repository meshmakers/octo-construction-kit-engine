using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     AB#5967 slice 2: the archive-level dump and load, the rollup validator and repair, and the restore-reconcile marker
///     on <see cref="IStreamDataRepository" />. A repository without them keeps working; the result records survive JSON.
/// </summary>
public class RollupConsistencyContractTests
{
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void RepositoryWithoutTheNewMembers_ThrowsTheDocumentedException()
    {
        IStreamDataRepository repo = new StreamDataRepositoryStub();
        var plan = new ArchiveCopyPlan(Rt, [], []);
        var location = new ArchiveCopyStagingLocation("file:///tmp/x");

        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.DumpArchiveToStagingAsync(Rt, location, null, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.LoadArchiveFromStagingAsync(Rt, plan, location, null, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.ValidateRollupsAsync(null, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.RepairRollupsAsync(null, true, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.IsRestoreReconcilePendingAsync(Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.SetRestoreReconcilePendingAsync(true, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.EnqueueRollupRepairAsync(null, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.GetRollupRepairRequestAsync("x", Ct); });
    }

    [Fact]
    public async Task RepairRequest_RoundTripsThroughJson_AndNoPendingRepairsMeansNothingRuns()
    {
        IStreamDataRepository repo = new StreamDataRepositoryStub();
        Assert.Equal(0, await repo.RunPendingRollupRepairsAsync(Ct));

        var empty = new RollupConsistencyReport([], new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var request = new RollupRepairRequest("r1", RollupRepairRequestState.Failed, empty.GeneratedAtUtc, empty.GeneratedAtUtc,
            [new RollupRepairAction(Rt, RollupRepairKind.DeleteOrphans, "d", RollupRepairStatus.Planned)],
            new RollupRepairReport(false, empty, [], empty, "step failed"));

        var back = JsonSerializer.Deserialize<RollupRepairRequest>(JsonSerializer.Serialize(request))!;

        Assert.Equal(RollupRepairRequestState.Failed, back.State);
        Assert.Equal("step failed", back.Report!.FailedStep);
        Assert.Equal(request.PlannedActions, back.PlannedActions);
    }

    [Fact]
    public async Task RepositoryThatDefersNoSweeps_ReportsNothingToDo()
    {
        IStreamDataRepository repo = new StreamDataRepositoryStub();

        Assert.Equal(0, await repo.SweepSupersededGenerationsAsync(Ct));
    }

    [Fact]
    public async Task ReadinessOfARepositoryWithoutTheCopy_SaysWhy_AndIsNotReady()
    {
        IStreamDataRepository repo = new StreamDataRepositoryStub();

        var readiness = await repo.CheckStagingCopyReadinessAsync(Ct);

        Assert.False(readiness.IsReady);
        Assert.False(readiness.Supported);
        Assert.Contains("does not support", readiness.Reason, StringComparison.Ordinal);
        Assert.True(new StagingCopyReadiness(true, true, "5.10.10", null).IsReady);
        Assert.False(new StagingCopyReadiness(true, false, "5.10.10", "filter").IsReady);
    }

    [Fact]
    public void DumpResult_RoundTripsThroughJson_AndKnowsWhetherItSucceeded()
    {
        var slice = ArchiveCopySlice.ForTimeRange(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 10);
        var map = ArchiveCopySlice.ForGenerationMap(3);
        var plan = new ArchiveCopyPlan(Rt, [], [slice, map]);
        var result = new ArchiveCopyDumpResult(Rt, plan,
            [new ArchiveCopySliceResult(slice, ArchiveCopySliceStatus.Succeeded, 10, 0, TimeSpan.FromSeconds(1)),
             new ArchiveCopySliceResult(map, ArchiveCopySliceStatus.Succeeded, 3, 0, TimeSpan.FromMilliseconds(5))],
            Attempts: 2,
            MapReadAtUtc: new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc),
            LastDataCopiedAtUtc: new DateTime(2026, 3, 1, 10, 0, 5, DateTimeKind.Utc));

        var back = JsonSerializer.Deserialize<ArchiveCopyDumpResult>(JsonSerializer.Serialize(result))!;

        Assert.True(result.IsSuccess);
        Assert.Equal(2, back.Attempts);
        Assert.Equal(result.MapReadAtUtc, back.MapReadAtUtc);
        Assert.Equal(result.LastDataCopiedAtUtc, back.LastDataCopiedAtUtc);
        Assert.Equal(result.Plan.Slices, back.Plan.Slices);
        Assert.Equal(result.Slices, back.Slices);
        Assert.False((result with { Slices = result.Slices.Take(1).ToList() }).IsSuccess); // a slice is missing
    }

    [Fact]
    public void LoadResult_IsOnlyASuccessWhenTheRowCountIsVerified()
    {
        var slice = ArchiveCopySlice.ForTimeRange(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 10);
        var ok = new ArchiveCopySliceResult(slice, ArchiveCopySliceStatus.Succeeded, 10, 0, TimeSpan.Zero);

        Assert.True(new ArchiveCopyLoadResult(Rt, [ok], 10, 10, true).IsSuccess);
        Assert.False(new ArchiveCopyLoadResult(Rt, [ok], 10, 9, false).IsSuccess);
        Assert.False(new ArchiveCopyLoadResult(Rt, [ok], 10, null, false).IsSuccess);

        var back = JsonSerializer.Deserialize<ArchiveCopyLoadResult>(
            JsonSerializer.Serialize(new ArchiveCopyLoadResult(Rt, [ok], 10, 10, true)))!;
        Assert.True(back.RowCountVerified);
        Assert.Equal(10, back.LoadedRows);
    }

    [Fact]
    public void ConsistencyAndRepairReports_RoundTripThroughJson_WithoutLoss()
    {
        var finding = new RollupConsistencyFinding(RollupConsistencyCheck.PointerWithoutRows, "No rows for generation 5.",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 5, 0);
        var gap = new RollupConsistencyFinding(RollupConsistencyCheck.WatermarkAheadOfRows, "Watermark ahead.",
            Gap: TimeSpan.FromHours(6));
        var before = new RollupConsistencyReport([new RollupConsistencyEntry(Rt, [finding, gap])],
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var after = new RollupConsistencyReport([new RollupConsistencyEntry(Rt, [])], new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc));
        var repair = new RollupRepairReport(false, before,
            [new RollupRepairAction(Rt, RollupRepairKind.RecomputePointerRange, "Recompute", RollupRepairStatus.Done,
                finding.RangeFromUtc, finding.RangeToUtc, 5)], after, null);

        var back = JsonSerializer.Deserialize<RollupRepairReport>(JsonSerializer.Serialize(repair))!;

        Assert.False(before.IsOk);
        Assert.True(after.IsOk);
        Assert.True(repair.IsOk);
        Assert.False(back.Before.IsOk);
        Assert.Equal(RollupConsistencyCheck.PointerWithoutRows, back.Before.Rollups.Single().Findings[0].Check);
        Assert.Equal(TimeSpan.FromHours(6), back.Before.Rollups.Single().Findings[1].Gap);
        Assert.Equal(repair.Actions, back.Actions);
        Assert.True(back.After!.IsOk);
    }

    [Fact]
    public void ARepairReport_IsOkOnlyWhenNothingFailedAndTheTenantValidatesOk()
    {
        var bad = new RollupConsistencyReport(
            [new RollupConsistencyEntry(Rt, [new RollupConsistencyFinding(RollupConsistencyCheck.MissingGenerationMap, "m")])],
            DateTime.UtcNow);
        var good = new RollupConsistencyReport([new RollupConsistencyEntry(Rt, [])], DateTime.UtcNow);

        Assert.True(new RollupRepairReport(true, bad, [], null, null).IsOk); // a dry run changed nothing and failed nowhere
        Assert.False(new RollupRepairReport(false, bad, [], bad, null).IsOk); // still inconsistent
        Assert.False(new RollupRepairReport(false, bad, [], good, "recompute of the range failed").IsOk);
        Assert.True(new RollupRepairReport(false, bad, [], good, null).IsOk);
    }

    [Fact]
    public void Options_AllowAtLeastOneMapAttempt()
    {
        new ArchiveCopyOptions().Validate();
        Assert.Equal(3, new ArchiveCopyOptions().MaxMapAttempts);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveCopyOptions { MaxMapAttempts = 0 }.Validate());
    }
}
