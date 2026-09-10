using System;
using System.Collections.Generic;
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

    // ---- Recompute-work purge on disable/delete (AB#4300) ----

    private static readonly OctoObjectId JobRt = OctoObjectId.GenerateNewId();
    private static readonly DateTime PurgeFrom = new(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PurgeTo = new(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);

    private ArchiveLifecycleService NewSutWithRecomputeStores(
        IArchiveRecomputeStateStore stateStore, IRecomputeJobStore jobStore) =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance,
            recomputeStateStore: stateStore, recomputeJobStore: jobStore);

    private static RecomputeJobSnapshot PendingJob() =>
        new(JobRt, Rt, RecomputeJobState.Pending, RecomputeTrigger.Manual,
            PurgeFrom, PurgeTo, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Disable_PurgesPendingRangesAndTerminatesActiveJob()
    {
        Stub(CkArchiveStatus.Activated);
        var stateStore = A.Fake<IArchiveRecomputeStateStore>();
        var jobStore = A.Fake<IRecomputeJobStore>();
        A.CallTo(() => jobStore.GetActiveForArchiveAsync(Rt)).Returns(PendingJob());

        await NewSutWithRecomputeStores(stateStore, jobStore).DisableAsync(Rt);

        A.CallTo(() => stateStore.ClearPendingRecomputeRangesAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => jobStore.UpdateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.State == RecomputeJobState.Failed && j.ErrorReason!.Contains("disabled"))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Delete_PurgesPendingRangesAndTerminatesActiveJob()
    {
        Stub(CkArchiveStatus.Activated);
        var stateStore = A.Fake<IArchiveRecomputeStateStore>();
        var jobStore = A.Fake<IRecomputeJobStore>();
        A.CallTo(() => jobStore.GetActiveForArchiveAsync(Rt)).Returns(PendingJob());

        await NewSutWithRecomputeStores(stateStore, jobStore).DeleteAsync(Rt);

        A.CallTo(() => stateStore.ClearPendingRecomputeRangesAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => jobStore.UpdateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.State == RecomputeJobState.Failed && j.ErrorReason!.Contains("deleted"))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Disable_NoActiveJob_ClearsRangesWithoutUpdatingJob()
    {
        Stub(CkArchiveStatus.Activated);
        var stateStore = A.Fake<IArchiveRecomputeStateStore>();
        var jobStore = A.Fake<IRecomputeJobStore>();
        A.CallTo(() => jobStore.GetActiveForArchiveAsync(Rt)).Returns((RecomputeJobSnapshot?)null);

        await NewSutWithRecomputeStores(stateStore, jobStore).DisableAsync(Rt);

        A.CallTo(() => stateStore.ClearPendingRecomputeRangesAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => jobStore.UpdateAsync(A<RecomputeJobSnapshot>._)).MustNotHaveHappened();
    }

    // ---- Initial-watermark seeding on activation (rollup-archives concept §4) ----

    private static readonly DateTime FixedNow = new(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);

    private ArchiveLifecycleService NewSutWithRollupAndClock(IRollupArchiveRuntimeStore rollupStore) =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance,
            rollupStore, clock: () => FixedNow);

    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SecondSourceRt = OctoObjectId.GenerateNewId();
    private static readonly DateTime Cutover = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RollupArchiveSnapshot RollupSnapshot(
        DateTime? watermark,
        TimeSpan? bucketSize = null,
        CkArchiveStatus status = CkArchiveStatus.Created,
        RollupSourceReference[]? sources = null)
    {
        return new RollupArchiveSnapshot(
            Rt, TargetType, status, null,
            sources ?? new[] { new RollupSourceReference(SourceRt) },
            bucketSize ?? TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            FrozenUntil: null);
    }

    /// <summary>The AB#5157 cutover shape: a legacy source before, and the native source from, the cutover.</summary>
    private static RollupSourceReference[] CutoverSources() =>
        new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: Cutover),
        };

    private void StubSourceActivatedWithVoltage() => StubSourceActivatedWithVoltage(SourceRt);

    private void StubSourceActivatedWithVoltage(OctoObjectId sourceRtId)
    {
        A.CallTo(() => _store.GetAsync(sourceRtId))
            .Returns(new ArchiveSnapshot(
                sourceRtId, TargetType, CkArchiveStatus.Activated, null,
                new[] { new CkArchiveColumnSpec("voltage", Indexed: true, Required: false) }));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
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

    // ---- Derived-columns self-heal on activation (AB#4772) ----

    [Fact]
    public async Task Activate_Rollup_PersistsDerivedColumnsViaStore()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt)).Returns(RollupSnapshot(watermark: null));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage();

        await NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt);

        A.CallTo(() => rollupStore.TryPersistDerivedColumnsAsync(Rt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Activate_Rollup_ColumnsHealFailure_DoesNotBlockActivation()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt)).Returns(RollupSnapshot(watermark: null));
        A.CallTo(() => rollupStore.TryPersistDerivedColumnsAsync(Rt))
            .Throws(new InvalidOperationException("boom"));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage();

        await NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt);

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

        var ex = await Assert.ThrowsAsync<RollupSourcePathMissingException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));
        Assert.Equal("voltage", ex.SourcePath);
        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
    }

    // ---- AB#5157: activation validates EVERY declared source ----

    [Fact]
    public async Task Activate_MultiSourceRollup_LoadsAndValidatesBothSources()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null, bucketSize: TimeSpan.FromDays(1), sources: CutoverSources()));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage(SourceRt);
        StubSourceActivatedWithVoltage(SecondSourceRt);

        await NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt);

        A.CallTo(() => _store.GetAsync(SourceRt)).MustHaveHappened();
        A.CallTo(() => _store.GetAsync(SecondSourceRt)).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
    }

    // TC-VAL-14: the second source is not activated.
    [Fact]
    public async Task Activate_MultiSourceRollup_SecondSourceNotActivated_Throws()
    {
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null, bucketSize: TimeSpan.FromDays(1), sources: CutoverSources()));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage(SourceRt);
        A.CallTo(() => _store.GetAsync(SecondSourceRt))
            .Returns(new ArchiveSnapshot(
                SecondSourceRt, TargetType, CkArchiveStatus.Created, null,
                new[] { new CkArchiveColumnSpec("voltage", true, false) }));

        var ex = await Assert.ThrowsAsync<RollupSourceNotActivatedException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));

        Assert.Equal(CkArchiveStatus.Created, ex.SourceStatus);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    // TC-VAL-11: the second source targets another CK type.
    [Fact]
    public async Task Activate_MultiSourceRollup_SecondSourceOtherTargetType_Throws()
    {
        var otherType = new RtCkId<CkTypeId>("Test", new CkTypeId("OtherType"));
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        A.CallTo(() => rollupStore.GetAsync(Rt))
            .Returns(RollupSnapshot(watermark: null, bucketSize: TimeSpan.FromDays(1), sources: CutoverSources()));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage(SourceRt);
        A.CallTo(() => _store.GetAsync(SecondSourceRt))
            .Returns(new ArchiveSnapshot(
                SecondSourceRt, otherType, CkArchiveStatus.Activated, null,
                new[] { new CkArchiveColumnSpec("voltage", true, false) }));

        var ex = await Assert.ThrowsAsync<RollupSourceTargetTypeMismatchException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));

        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<ArchiveSnapshot>._)).MustNotHaveHappened();
    }

    // TC-VAL-18: a transitive cycle in the stored rollup graph blocks activation.
    [Fact]
    public async Task Activate_Rollup_TransitiveCycle_Throws()
    {
        var sourceRollupRt = SourceRt;
        var rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
        var rollup = RollupSnapshot(watermark: null);
        // The source is itself a rollup that reads this rollup back — a two-step cycle.
        var sourceRollup = new RollupArchiveSnapshot(
            sourceRollupRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(Rt) },
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }, null);
        A.CallTo(() => rollupStore.GetAsync(Rt)).Returns(rollup);
        A.CallTo(() => rollupStore.GetAsync(sourceRollupRt)).Returns(sourceRollup);
        A.CallTo(() => rollupStore.EnumerateAsync()).Returns(ToAsync(new[] { rollup, sourceRollup }));
        Stub(CkArchiveStatus.Created);
        StubSourceActivatedWithVoltage(sourceRollupRt);

        var ex = await Assert.ThrowsAsync<RollupSourceCycleException>(
            () => NewSutWithRollupAndClock(rollupStore).ActivateAsync(Rt));

        Assert.Equal(sourceRollupRt, ex.SourceArchiveRtId);
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    // ---- AB#5157: coverage cache invalidation on delete ----

    [Fact]
    public async Task Delete_InvalidatesTheArchivesCoverageMemo()
    {
        var invalidator = A.Fake<IArchiveCoverageInvalidator>();
        Stub(CkArchiveStatus.Activated);

        var sut = new ArchiveLifecycleService(
            TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance,
            coverageInvalidator: invalidator);
        await sut.DeleteAsync(Rt);

        A.CallTo(() => invalidator.Invalidate(TenantId, Rt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Delete_WithoutCoverageInvalidator_StillDeletes()
    {
        Stub(CkArchiveStatus.Activated);

        await NewSut().DeleteAsync(Rt);

        A.CallTo(() => _repo.DeleteArchiveAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.ArchiveEntityAsync(Rt)).MustHaveHappenedOnceExactly();
    }
}
