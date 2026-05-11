using System;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupArchiveLifecycleServiceTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));

    private readonly ICkRollupArchiveRuntimeStore _store = A.Fake<ICkRollupArchiveRuntimeStore>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    private RollupArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _audit, NullLogger<RollupArchiveLifecycleService>.Instance);

    private static CkRollupArchiveSnapshot Snapshot(
        DateTime? frozenUntil = null,
        DateTime? watermark = null,
        TimeSpan? bucketSize = null) =>
        new(
            Rt,
            TargetType,
            CkArchiveStatus.Activated,
            null,
            SourceRt,
            bucketSize ?? TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            frozenUntil);

    // ---- Freeze ----

    [Fact]
    public async Task Freeze_FromUnfrozen_PersistsAndAudits()
    {
        var until = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot());

        await NewSut().FreezeAsync(Rt, until);

        A.CallTo(() => _store.SetFrozenUntilAsync(Rt, until)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordFreezeAsync(TenantId, Rt, until, null)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Freeze_MovingForward_PersistsAndAudits()
    {
        var current = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
        var newer = current.AddHours(4);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: current));

        await NewSut().FreezeAsync(Rt, newer);

        A.CallTo(() => _store.SetFrozenUntilAsync(Rt, newer)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordFreezeAsync(TenantId, Rt, newer, null)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Freeze_Backwards_ThrowsAndDoesNotWrite()
    {
        var current = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        var earlier = current.AddHours(-2);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: current));

        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(
            () => NewSut().FreezeAsync(Rt, earlier));

        A.CallTo(() => _store.SetFrozenUntilAsync(A<OctoObjectId>._, A<DateTime?>._)).MustNotHaveHappened();
        A.CallTo(() => _audit.RecordFreezeAsync(A<string>._, A<OctoObjectId>._, A<DateTime>._, A<string?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Freeze_UnknownRollup_ThrowsArchiveNotFoundException()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Task.FromResult<CkRollupArchiveSnapshot?>(null));

        await Assert.ThrowsAsync<ArchiveNotFoundException>(
            () => NewSut().FreezeAsync(Rt, DateTime.UtcNow));
    }

    // ---- Unfreeze ----

    [Fact]
    public async Task Unfreeze_WhenFrozen_ClearsFrozenUntil()
    {
        var current = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: current));

        await NewSut().UnfreezeAsync(Rt);

        A.CallTo(() => _store.SetFrozenUntilAsync(Rt, null)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Unfreeze_NotFrozen_NoOps()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: null));

        await NewSut().UnfreezeAsync(Rt);

        A.CallTo(() => _store.SetFrozenUntilAsync(A<OctoObjectId>._, A<DateTime?>._)).MustNotHaveHappened();
    }

    // ---- Rewind watermark ----

    [Fact]
    public async Task Rewind_TruncatesToBucketBoundary_AndAllowsRewind()
    {
        // BucketSize = 1 min; passing 14:00:42 → should truncate to 14:00:00.
        var passedIn = new DateTime(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);
        var expectedBucketEnd = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(bucketSize: TimeSpan.FromMinutes(1)));

        await NewSut().RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, expectedBucketEnd, true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Rewind_ZeroBucketSize_PassesTimestampUnchanged()
    {
        // Defensive: a bucket-size of 0 should not divide-by-zero; pass the value through.
        var passedIn = new DateTime(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(bucketSize: TimeSpan.Zero));

        await NewSut().RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, passedIn, true))
            .MustHaveHappenedOnceExactly();
    }
}
