using System;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class ArchiveLifecycleServiceTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("TempSensor"));

    private readonly IArchiveRuntimeStore _store = A.Fake<IArchiveRuntimeStore>();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    private ArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance);

    private void Stub(CkArchiveStatus status) =>
        A.CallTo(() => _store.GetAsync(Rt))
            .Returns(new ArchiveSnapshot(Rt, TargetType, status, null, Array.Empty<CkArchiveColumnSpec>()));

    [Fact]
    public async Task Activate_FromCreated_ProvisionsCrateThenSetsActivated()
    {
        Stub(CkArchiveStatus.Created);
        await NewSut().ActivateAsync(Rt);

        // Crate first, store last (concept §11 ordering check via call-order on fakes).
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>.That.Matches(s => s.RtId == Rt))).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Created, CkArchiveStatus.Activated, null))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Activate_FromDisabled_RunsDdlAndTransitions()
    {
        Stub(CkArchiveStatus.Disabled);
        await NewSut().ActivateAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>.That.Matches(s => s.RtId == Rt))).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Fact]
    public async Task Activate_FromFailed_RetriesDdlAndTransitions()
    {
        Stub(CkArchiveStatus.Failed);
        await NewSut().ActivateAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>.That.Matches(s => s.RtId == Rt))).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Fact]
    public async Task Activate_AlreadyActivated_IsNoop()
    {
        Stub(CkArchiveStatus.Activated);
        await NewSut().ActivateAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Activate_DdlFails_FlipsToFailedAndThrowsActivationFailedException()
    {
        Stub(CkArchiveStatus.Created);
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>.That.Matches(s => s.RtId == Rt)))
            .Throws(new InvalidOperationException("crate boom"));

        await Assert.ThrowsAsync<ArchiveActivationFailedException>(() => NewSut().ActivateAsync(Rt));

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Failed)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Created, CkArchiveStatus.Failed, "crate boom"))
            .MustHaveHappenedOnceExactly();
        // Status was never set to Activated.
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Disable_FromActivated_FlipsStatusOnly_NoCrateOps()
    {
        Stub(CkArchiveStatus.Activated);
        await NewSut().DisableAsync(Rt);

        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Disabled)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _repo.DeleteArchiveAsync(A<OctoObjectId>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Disable_FromDisabled_IsNoop()
    {
        Stub(CkArchiveStatus.Disabled);
        await NewSut().DisableAsync(Rt);

        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Failed)]
    public async Task Disable_IllegalSource_Throws(CkArchiveStatus from)
    {
        Stub(from);
        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(() => NewSut().DisableAsync(Rt));
    }

    [Fact]
    public async Task Enable_IsAliasForActivate()
    {
        Stub(CkArchiveStatus.Disabled);
        await NewSut().EnableAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>.That.Matches(s => s.RtId == Rt))).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Fact]
    public async Task RetryActivation_FromFailed_RunsDdlAndTransitions()
    {
        Stub(CkArchiveStatus.Failed);
        await NewSut().RetryActivationAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>.That.Matches(s => s.RtId == Rt))).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Activated)]
    [InlineData(CkArchiveStatus.Disabled)]
    public async Task RetryActivation_NotFromFailed_Throws(CkArchiveStatus from)
    {
        Stub(from);
        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(
            () => NewSut().RetryActivationAsync(Rt));
    }

    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Activated)]
    [InlineData(CkArchiveStatus.Disabled)]
    [InlineData(CkArchiveStatus.Failed)]
    public async Task Delete_FromAnyState_DropsCrateThenArchivesEntity(CkArchiveStatus from)
    {
        Stub(from);
        await NewSut().DeleteAsync(Rt);

        A.CallTo(() => _repo.DeleteArchiveAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.ArchiveEntityAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordDeletionAsync(TenantId, Rt, from)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Activate_UnknownArchive_ThrowsArchiveNotFoundException()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Task.FromResult<ArchiveSnapshot?>(null));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => NewSut().ActivateAsync(Rt));
    }

    // ---- Source-delete guard (rollup-archives concept §6 / §10) ----

    private ArchiveLifecycleService NewSutWithRollupStore(IRollupArchiveRuntimeStore rollupStore) =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance, rollupStore);

    [Fact]
    public async Task Delete_NoRollupStore_DeletesWithoutGuard()
    {
        // The default-constructed service has no rollup store; deletes proceed regardless.
        Stub(CkArchiveStatus.Activated);

        await NewSut().DeleteAsync(Rt);

        A.CallTo(() => _repo.DeleteArchiveAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.ArchiveEntityAsync(Rt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Delete_WithRollupStore_NoDependentRollups_Proceeds()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.CountActiveRollupsForSourceAsync(Rt)).Returns(0);
        Stub(CkArchiveStatus.Activated);

        await NewSutWithRollupStore(rollupStore).DeleteAsync(Rt);

        A.CallTo(() => _repo.DeleteArchiveAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.ArchiveEntityAsync(Rt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Delete_WithRollupStore_DependentRollupsExist_ThrowsAndPreservesArchive()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.CountActiveRollupsForSourceAsync(Rt)).Returns(2);
        Stub(CkArchiveStatus.Activated);

        var ex = await Assert.ThrowsAsync<RollupSourceInUseException>(
            () => NewSutWithRollupStore(rollupStore).DeleteAsync(Rt));
        Assert.Equal(2, ex.DependentRollupCount);

        // Neither the Crate table nor the entity must be touched when the guard fires.
        A.CallTo(() => _repo.DeleteArchiveAsync(A<OctoObjectId>._)).MustNotHaveHappened();
        A.CallTo(() => _store.ArchiveEntityAsync(A<OctoObjectId>._)).MustNotHaveHappened();
        A.CallTo(() => _audit.RecordDeletionAsync(A<string>._, A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    // ---- Initial-watermark seeding on activation (rollup-archives concept §4) ----

    private static readonly DateTime FixedNow = new(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);

    private ArchiveLifecycleService NewSutWithRollupAndClock(IRollupArchiveRuntimeStore rollupStore) =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance,
            rollupStore, () => FixedNow);

    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();

    private static RollupArchiveSnapshot RollupSnapshot(
        DateTime? watermark,
        TimeSpan? bucketSize = null,
        CkArchiveStatus status = CkArchiveStatus.Created)
    {
        return new RollupArchiveSnapshot(
            Rt, TargetType, status, null,
            SourceRt,
            bucketSize ?? TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            FrozenUntil: null);
    }

    private void StubSourceActivatedWithVoltage()
    {
        A.CallTo(() => _store.GetAsync(SourceRt))
            .Returns(new ArchiveSnapshot(
                SourceRt, TargetType, CkArchiveStatus.Activated, null,
                new[] { new CkArchiveColumnSpec("voltage", Indexed: true, Required: false) }));
    }

    [Fact]
    public async Task Activate_RollupWithNullWatermark_SeedsToPreviousBucketBoundary()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null, bucketSize: TimeSpan.FromMinutes(1)));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage();

        await NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt);

        // now = 14:00:42; bucketSize = 1m; (now - bucketSize) = 13:59:42; truncate-down to
        // bucket boundary = 13:59:00.
        var expected = new DateTime(2026, 5, 11, 13, 59, 0, DateTimeKind.Utc);
        A.CallTo(() => rollupStore.AdvanceWatermarkAsync(Rt, expected, A<bool>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Activate_RollupWithExistingWatermark_PreservesIt()
    {
        var existing = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: existing));
        Stub(CkArchiveStatus.Disabled);
        StubSourceActivatedWithVoltage();

        await NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt);

        A.CallTo(() => rollupStore.AdvanceWatermarkAsync(A<OctoObjectId>._, A<DateTime>._, A<bool>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Activate_NonRollup_DoesNotTouchRollupStore()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(Task.FromResult<RollupArchiveSnapshot?>(null));
        Stub(CkArchiveStatus.Created);

        await NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt);

        A.CallTo(() => rollupStore.AdvanceWatermarkAsync(A<OctoObjectId>._, A<DateTime>._, A<bool>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Activate_NoRollupStore_SkipsSeeding()
    {
        // Default-constructed service: no rollup store; nothing rollup-related happens.
        Stub(CkArchiveStatus.Created);

        await NewSut().ActivateAsync(Rt);

        // No exception, archive transitions through normally.
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
    }

    // ---- Activation-time rollup validation (rollup-archives concept §10) ----

    [Fact]
    public async Task Activate_Rollup_SourceMissing_ThrowsAndDoesNotProvision()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null));
        Stub(CkArchiveStatus.Created);
        A.CallTo(() => _store.GetAsync(SourceRt))
            .Returns(Task.FromResult<ArchiveSnapshot?>(null));

        await Assert.ThrowsAsync<RollupSourceMissingException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Activate_Rollup_SourceNotActivated_Throws()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null));
        Stub(CkArchiveStatus.Created);
        A.CallTo(() => _store.GetAsync(SourceRt))
            .Returns(new ArchiveSnapshot(
                SourceRt, TargetType, CkArchiveStatus.Disabled, null,
                new[] { new CkArchiveColumnSpec("voltage", true, false) }));

        var ex = await Assert.ThrowsAsync<RollupSourceNotActivatedException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));
        Assert.Equal(CkArchiveStatus.Disabled, ex.SourceStatus);
    }

    [Fact]
    public async Task Activate_Rollup_SourcePathMissing_Throws()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null));
        Stub(CkArchiveStatus.Created);
        // Source archive is activated but doesn't capture "voltage" — only "current".
        A.CallTo(() => _store.GetAsync(SourceRt))
            .Returns(new ArchiveSnapshot(
                SourceRt, TargetType, CkArchiveStatus.Activated, null,
                new[] { new CkArchiveColumnSpec("current", true, false) }));

        var ex = await Assert.ThrowsAsync<RollupSourcePathInvalidException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));
        Assert.Equal("voltage", ex.SourcePath);
    }
}
