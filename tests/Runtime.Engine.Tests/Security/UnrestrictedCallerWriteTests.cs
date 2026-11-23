using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     A caller without a visibility question (system session, tenant without policies, full read grant) gets the
///     answers it always got: an invalid payload is a validation error whether the record exists or not, a valid change
///     of a missing record is not found, and a valid change set is written without an existence read.
/// </summary>
public class UnrestrictedCallerWriteTests : HiddenOrMissingWriteFixture
{
    public enum UnrestrictedCaller
    {
        SystemSession,
        TenantWithoutPolicies,
        FullReadGrant
    }

    private IOctoSession SessionOf(UnrestrictedCaller caller) =>
        caller == UnrestrictedCaller.SystemSession ? SystemSession() : UserSession(Full);

    private HiddenOrMissingTestRepository RepositoryOf(UnrestrictedCaller caller) =>
        Repository(caller == UnrestrictedCaller.TenantWithoutPolicies ? RtDataPolicyTable.Empty : PolicyTable());

    public static TheoryData<UnrestrictedCaller, string> CallersAndPayloadOperations()
    {
        var data = new TheoryData<UnrestrictedCaller, string>();
        foreach (var caller in Enum.GetValues<UnrestrictedCaller>())
        {
            data.Add(caller, "update");
            data.Add(caller, "replace");
        }

        return data;
    }

    public static TheoryData<UnrestrictedCaller> Callers()
    {
        var data = new TheoryData<UnrestrictedCaller>();
        foreach (var caller in Enum.GetValues<UnrestrictedCaller>())
        {
            data.Add(caller);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CallersAndPayloadOperations))]
    public async Task InvalidPayload_ExistingAndMissingRecord_AnswerTheValidationError(UnrestrictedCaller caller,
        string operation)
    {
        var repository = RepositoryOf(caller);
        var storedRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var existingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, SessionOf(caller),
            [Change(operation, storedRecord.RtId, false)]), storedRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, SessionOf(caller),
            [Change(operation, missingRtId, false)]), missingRtId);

        AssertValidationError(existingAnswer, MandatoryTitleMessageNumberOf(operation));
        AssertValidationError(missingAnswer, MandatoryTitleMessageNumberOf(operation));
    }

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task ValidChangeOfMissingRecord_AnswersNotFound(UnrestrictedCaller caller)
    {
        foreach (var operation in new[] { "update", "replace", "delete" })
        {
            var missingRtId = OctoObjectId.GenerateNewId();

            var answer = await AnswerOfAsync(() => ApplyAsync(RepositoryOf(caller), SessionOf(caller),
                [Change(operation, missingRtId, true)]), missingRtId);

            AssertNotFound(answer);
        }
    }

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task InvalidAssociationFromMissingOrigin_AnswersTheValidationError(UnrestrictedCaller caller)
    {
        var missingRtId = OctoObjectId.GenerateNewId();

        var answer = await AnswerOfAsync(() => ApplyAsync(RepositoryOf(caller), SessionOf(caller), [],
            [AssociationFrom(missingRtId, false)]), missingRtId);

        AssertValidationError(answer, InvalidRoleMessageNumber);
    }

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task ValidUpdates_AreWrittenWithoutAnExistenceRead(UnrestrictedCaller caller)
    {
        var storedRecords = Enumerable.Range(0, 5).Select(_ => Store(OtherUser)).ToList();

        await ApplyAsync(RepositoryOf(caller), SessionOf(caller),
            storedRecords.Select(storedRecord => Change("update", storedRecord.RtId, true)).ToList());

        A.CallTo(() => Collection.DocumentAsync(A<IOctoSession>._, A<OctoObjectId>._)).MustNotHaveHappened();
        A.CallTo(() => Collection.DocumentsAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .MustNotHaveHappened();
        A.CallTo(() => Collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
    }
}
