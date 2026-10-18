using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     AB#5967 (slice AB#6967): the contract of the staging copy on <see cref="IStreamDataRepository" />, exercised
///     with an in-memory fake the way the bot drives it — plan, dump slice by slice, load slice by slice.
/// </summary>
public class ArchiveCopyContractTests
{
    private const string CanarySecret = "CANARY/SECRET+value==";
    private const string CanarySas = "CANARYSAS";

    private static readonly OctoObjectId Source = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId Target = OctoObjectId.GenerateNewId();

    private static readonly ArchiveCopyStagingLocation Location =
        new($"s3://CANARYKEYID:{CanarySecret}@minio.octo-infra:9000/octo-artifacts/copy-staging/t1/run1",
            new Dictionary<string, string> { ["protocol"] = "http", ["sas_token"] = CanarySas });

    private readonly FakeStagingCopyStreamDataRepository _repo = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Day(int day, int hour = 0) => new(2026, 3, day, hour, 0, 0, DateTimeKind.Utc);

    private FakeStagingCopyStreamDataRepository RepoWithThreeDays()
    {
        _repo.AddArchive(Source,
        [
            (Day(1, 1), 1), (Day(1, 9), 2),
            (Day(2, 3), 3), (Day(2, 4), 4), (Day(2, 23), 5),
            (Day(4, 12), 6)
        ]);
        _repo.AddArchive(Target, []);
        return _repo;
    }

    private static async Task<List<ArchiveCopySliceResult>> Collect(IAsyncEnumerable<ArchiveCopySliceResult> results)
    {
        var list = new List<ArchiveCopySliceResult>();
        await foreach (var result in results)
        {
            list.Add(result);
        }

        return list;
    }

    [Fact]
    public void RepositoryWithoutStaging_ReportsNoSupport_AndEveryMemberThrowsTheDocumentedException()
    {
        IStreamDataRepository repo = new StreamDataRepositoryStub();
        var plan = new ArchiveCopyPlan(Source, [], []);

        Assert.False(repo.SupportsStagingCopy);
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.PlanArchiveCopyAsync(Source, null, Ct); });
        Assert.Throws<ArchiveCopyNotSupportedException>(() =>
        {
            _ = repo.CopyToStagingAsync(Source, plan, Location, null, Ct);
        });
        Assert.Throws<ArchiveCopyNotSupportedException>(() =>
        {
            _ = repo.CopyFromStagingAsync(Source, plan, Location, null, Ct);
        });
        Assert.Throws<ArchiveCopyNotSupportedException>(() =>
        {
            _ = repo.DescribeArchiveTableAsync(Source, ArchiveCopyTable.Data, Ct);
        });
        Assert.Throws<ArchiveCopyNotSupportedException>(() => { _ = repo.GetStorageVersionAsync(Ct); });
    }

    [Fact]
    public async Task Plan_CutsTheArchiveIntoCountedSlicesInAscendingOrder()
    {
        var plan = await RepoWithThreeDays().PlanArchiveCopyAsync(Source, null, Ct);

        Assert.Equal(Source, plan.ArchiveRtId);
        Assert.Equal([2L, 3L, 1L], plan.Slices.Select(s => s.RowCount));
        Assert.Equal(plan.Slices.OrderBy(s => s.FromUtc), plan.Slices);
        Assert.Equal(6, plan.TotalRows);
        Assert.Equal(["timestamp", "value"], plan.Tables.Single().Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task DumpThenLoad_WorksSliceBySlice_InPlanOrder_IntoAnotherArchive()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);

        var dumped = await Collect(repo.CopyToStagingAsync(Source, plan, Location, null, Ct));
        var loaded = await Collect(repo.CopyFromStagingAsync(Target, plan, Location, null, Ct));

        var keys = plan.Slices.Select(s => s.Key).ToList();
        Assert.Equal(keys.Select(k => $"to:{k}").Concat(keys.Select(k => $"from:{k}")), repo.Events);
        Assert.All(dumped.Concat(loaded), r => Assert.True(r.IsSuccess));
        Assert.Equal(plan.Slices.Select(s => s.RowCount), dumped.Select(r => r.CopiedRows));
        Assert.Equal(plan.Slices.Select(s => s.RowCount), loaded.Select(r => r.CopiedRows));
        Assert.All(dumped.Concat(loaded), r => Assert.Equal(0, r.RowCountDifference));
        Assert.Equal(repo.Rows(Source), repo.Rows(Target));
        Assert.False(repo.BulkSettingsActive);
        Assert.True(repo.Optimized);
        // the files lie where the concept puts them: <rtId>/data/<slice>
        Assert.All(repo.StoredFolders, f => Assert.Contains($"/{Source}/data/", f));
    }

    [Fact]
    public async Task FailingSlice_EndsTheCopyWithARedactedMessage_AndEarlierSlicesStayValid()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);
        repo.FailOnSliceKey = plan.Slices[1].Key;

        var dumped = await Collect(repo.CopyToStagingAsync(Source, plan, Location, null, Ct));

        Assert.Equal([ArchiveCopySliceStatus.Succeeded, ArchiveCopySliceStatus.Failed], dumped.Select(r => r.Status));
        Assert.False(dumped[1].IsSuccess);
        Assert.DoesNotContain("CANARY", dumped[1].Message!);
        Assert.Contains("access denied", dumped[1].Message!);
        Assert.Single(repo.StoredFolders); // the third slice was never copied
        Assert.Contains(Location.WithSubPath(plan.Slices[0].GetStagingPath(Source)).RedactedUri, repo.StoredFolders);
    }

    [Fact]
    public async Task Cancellation_StopsTheCopyBetweenTwoSlices()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var done = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in repo.CopyToStagingAsync(Source, plan, Location, null, cts.Token))
            {
                done++;
                await cts.CancelAsync(); // the job is cancelled while it handles the first result
            }
        });

        Assert.Equal(1, done);
        Assert.Single(repo.StoredFolders);
    }

    [Fact]
    public async Task Refusal_WhenTheJobLogWouldRecordTheCredential_BeforeAnySliceMoves()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);
        repo.JobLogFiltered = false;

        var ex = await Assert.ThrowsAsync<ArchiveCopyRefusedException>(() =>
            Collect(repo.CopyToStagingAsync(Source, plan, Location, null, Ct)));

        Assert.Equal(ArchiveCopyRefusalReason.JobLogNotFiltered, ex.Reason);
        Assert.Contains("jobs_log_filter", ex.Message);
        Assert.Empty(repo.Events);
        Assert.Empty(repo.StoredFolders);
    }

    [Fact]
    public async Task Load_StopsBeforeLoading_WhenADumpedColumnIsMissingInTheTable()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);
        await Collect(repo.CopyToStagingAsync(Source, plan, Location, null, Ct));
        repo.AddArchive(Target, [], ["timestamp"]); // the new table lacks 'value'

        var ex = await Assert.ThrowsAsync<ArchiveCopyRefusedException>(() =>
            Collect(repo.CopyFromStagingAsync(Target, plan, Location, null, Ct)));

        Assert.Equal(ArchiveCopyRefusalReason.ColumnMissing, ex.Reason);
        Assert.Contains("'value'", ex.Message);
        Assert.Empty(repo.Rows(Target));
        Assert.DoesNotContain(repo.Events, e => e.StartsWith("from:"));
        Assert.False(repo.BulkSettingsActive);
    }

    [Fact]
    public async Task Load_FailureInTheMiddle_EndsTheCopy_AndTheBulkSettingsAreBack()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);
        await Collect(repo.CopyToStagingAsync(Source, plan, Location, null, Ct));
        repo.FailOnSliceKey = plan.Slices[1].Key;

        var loaded = await Collect(repo.CopyFromStagingAsync(Target, plan, Location, null, Ct));

        Assert.Equal([ArchiveCopySliceStatus.Succeeded, ArchiveCopySliceStatus.Failed], loaded.Select(r => r.Status));
        Assert.DoesNotContain("CANARY", loaded[1].Message!);
        Assert.False(repo.BulkSettingsActive);
        Assert.False(repo.Optimized); // no merge after a failed load
        Assert.Equal(2, repo.Rows(Target).Count); // only the first slice
    }

    [Fact]
    public async Task Load_StoppedByTheCaller_PutsTheBulkSettingsBack()
    {
        var repo = RepoWithThreeDays();
        var plan = await repo.PlanArchiveCopyAsync(Source, null, Ct);
        await Collect(repo.CopyToStagingAsync(Source, plan, Location, null, Ct));

        await using (var enumerator = repo.CopyFromStagingAsync(Target, plan, Location, null, Ct).GetAsyncEnumerator(Ct))
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(repo.BulkSettingsActive); // during the load
        } // the caller walks away after the first slice

        Assert.False(repo.BulkSettingsActive);
    }
}
