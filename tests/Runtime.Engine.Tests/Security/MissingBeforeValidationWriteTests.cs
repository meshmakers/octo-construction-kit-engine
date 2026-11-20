using Meshmakers.Octo.ConstructionKit.Contracts;
using FakeItEasy;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     Without a visibility question (system session, tenant without policies) a missing target of an update,
///     replace, delete or association change answers not found before rule validation; an existing record keeps its
///     validation error. The existence check costs one read per type, not one per record, and reuses what the
///     data-permission guard already read.
/// </summary>
public class MissingBeforeValidationWriteTests : HiddenOrMissingWriteFixture
{
    public static TheoryData<bool, string> UnguardedCallers()
    {
        var data = new TheoryData<bool, string>();
        foreach (var systemSession in new[] { true, false })
        {
            foreach (var operation in new[] { "update", "replace" })
            {
                data.Add(systemSession, operation);
            }
        }

        return data;
    }

    private IOctoSession UnguardedSession(bool systemSession) =>
        systemSession ? SystemSession() : UserSession(WriterOwnedRead);

    private HiddenOrMissingTestRepository UnguardedRepository(bool systemSession) =>
        Repository(systemSession ? PolicyTable() : RtDataPolicyTable.Empty);

    [Theory]
    [MemberData(nameof(UnguardedCallers))]
    public async Task MissingRecord_InvalidPayload_AnswersNotFound(bool systemSession, string operation)
    {
        var missingRtId = OctoObjectId.GenerateNewId();

        var answer = await AnswerOfAsync(() => ApplyAsync(UnguardedRepository(systemSession),
            UnguardedSession(systemSession), [Change(operation, missingRtId, false)]), missingRtId);

        AssertNotFound(answer);
    }

    [Theory]
    [MemberData(nameof(UnguardedCallers))]
    public async Task ExistingRecord_InvalidPayload_KeepsTheValidationError(bool systemSession, string operation)
    {
        var storedRecord = Store(OtherUser);

        var answer = await AnswerOfAsync(() => ApplyAsync(UnguardedRepository(systemSession),
            UnguardedSession(systemSession), [Change(operation, storedRecord.RtId, false)]), storedRecord.RtId);

        AssertValidationError(answer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingRecord_ValidPayloadAndDelete_AnswerNotFound(bool systemSession)
    {
        foreach (var operation in new[] { "update", "replace", "delete" })
        {
            var missingRtId = OctoObjectId.GenerateNewId();

            var answer = await AnswerOfAsync(() => ApplyAsync(UnguardedRepository(systemSession),
                UnguardedSession(systemSession), [Change(operation, missingRtId, true)]), missingRtId);

            AssertNotFound(answer);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssociationChange_MissingOrigin_InvalidAssociation_AnswersNotFound(bool systemSession)
    {
        var missingRtId = OctoObjectId.GenerateNewId();

        var answer = await AnswerOfAsync(() => ApplyAsync(UnguardedRepository(systemSession),
            UnguardedSession(systemSession), [], [AssociationFrom(missingRtId, false)]), missingRtId);

        AssertNotFound(answer);
    }

    [Fact]
    public async Task AssociationChange_OriginInsertedInTheSameChangeSet_IsNotReportedMissing()
    {
        var newRtId = OctoObjectId.GenerateNewId();

        var answer = await AnswerOfAsync(() => ApplyAsync(Repository(RtDataPolicyTable.Empty), SystemSession(),
            [EntityUpdateInfo<RtEntity>.CreateInsert(Payload(newRtId, true))], [AssociationFrom(newRtId, false)]),
            newRtId);

        AssertValidationError(answer);
    }

    [Fact]
    public async Task ValidUpdatesOfExistingRecords_AreWritten_WithOneExistenceReadForTheWholeType()
    {
        var storedRecords = Enumerable.Range(0, 5).Select(_ => Store(OtherUser)).ToList();

        await ApplyAsync(Repository(RtDataPolicyTable.Empty), SystemSession(),
            storedRecords.Select(record => Change("update", record.RtId, true)).ToList());

        A.CallTo(() => Collection.DocumentsAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => Collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RecordsTheGuardAlreadyRead_AreNotReadAgainForExistence()
    {
        var ownRecord = Store(SubjectOf(WriterOwnedRead));

        await ApplyAsync(Repository(PolicyTable()), UserSession(WriterOwnedRead),
            [Change("update", ownRecord.RtId, true)]);

        A.CallTo(() => Collection.DocumentAsync(A<IOctoSession>._, ownRecord.RtId)).MustHaveHappenedOnceExactly();
        A.CallTo(() => Collection.DocumentsAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .MustNotHaveHappened();
    }
}
