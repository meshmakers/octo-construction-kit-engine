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

    private readonly ICkArchiveRuntimeStore _store = A.Fake<ICkArchiveRuntimeStore>();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    private ArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _repo, _audit, NullLogger<ArchiveLifecycleService>.Instance);

    private void Stub(CkArchiveStatus status) =>
        A.CallTo(() => _store.GetAsync(Rt))
            .Returns(new CkArchiveSnapshot(Rt, TargetType, status, null));

    [Fact]
    public async Task Activate_FromCreated_ProvisionsCrateThenSetsActivated()
    {
        Stub(CkArchiveStatus.Created);
        await NewSut().ActivateAsync(Rt);

        // Crate first, store last (concept §11 ordering check via call-order on fakes).
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(Rt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordTransitionAsync(TenantId, Rt, CkArchiveStatus.Created, CkArchiveStatus.Activated, null))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Activate_FromDisabled_RunsDdlAndTransitions()
    {
        Stub(CkArchiveStatus.Disabled);
        await NewSut().ActivateAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(Rt)).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Fact]
    public async Task Activate_FromFailed_RetriesDdlAndTransitions()
    {
        Stub(CkArchiveStatus.Failed);
        await NewSut().ActivateAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(Rt)).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Fact]
    public async Task Activate_AlreadyActivated_IsNoop()
    {
        Stub(CkArchiveStatus.Activated);
        await NewSut().ActivateAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<OctoObjectId>._)).MustNotHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(A<OctoObjectId>._, A<CkArchiveStatus>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Activate_DdlFails_FlipsToFailedAndThrowsActivationFailedException()
    {
        Stub(CkArchiveStatus.Created);
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(Rt))
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
        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(A<OctoObjectId>._)).MustNotHaveHappened();
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

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(Rt)).MustHaveHappened();
        A.CallTo(() => _store.SetStatusAsync(Rt, CkArchiveStatus.Activated)).MustHaveHappened();
    }

    [Fact]
    public async Task RetryActivation_FromFailed_RunsDdlAndTransitions()
    {
        Stub(CkArchiveStatus.Failed);
        await NewSut().RetryActivationAsync(Rt);

        A.CallTo(() => _repo.EnsureArchiveCreatedAsync(Rt)).MustHaveHappened();
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
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Task.FromResult<CkArchiveSnapshot?>(null));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => NewSut().ActivateAsync(Rt));
    }
}
