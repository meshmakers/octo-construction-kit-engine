using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     An update, replace or delete of a record the caller may not see answers exactly like the same call with a
///     missing rtId, whatever the payload: existence and visibility are resolved before rule validation, so a
///     validation error never tells a hidden record from a missing one.
/// </summary>
public class HiddenEqualsMissingWriteTests : HiddenOrMissingWriteFixture
{
    public static TheoryData<string, string, bool> RestrictedCallerChanges()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var role in new[] { WriterOwnedRead, WriteOnly })
        {
            foreach (var operation in new[] { "update", "replace" })
            {
                data.Add(role, operation, true);
                data.Add(role, operation, false);
            }

            data.Add(role, "delete", true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RestrictedCallerChanges))]
    public async Task HiddenRecord_AnswersExactlyLikeMissingRecord(string role, string operation, bool validPayload)
    {
        var repository = Repository(PolicyTable());
        var hiddenRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var hiddenAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
            [Change(operation, hiddenRecord.RtId, validPayload)]), hiddenRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
            [Change(operation, missingRtId, validPayload)]), missingRtId);

        AssertNotFound(hiddenAnswer);
        Assert.Equal(hiddenAnswer, missingAnswer);
    }

    [Theory]
    [InlineData(WriterOwnedRead, true)]
    [InlineData(WriterOwnedRead, false)]
    [InlineData(WriteOnly, true)]
    [InlineData(WriteOnly, false)]
    public async Task AssociationChange_HiddenOrigin_AnswersExactlyLikeMissingOrigin(string role, bool validAssociation)
    {
        var repository = Repository(PolicyTable());
        var hiddenOrigin = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var hiddenAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role), [],
            [AssociationFrom(hiddenOrigin.RtId, validAssociation)]), hiddenOrigin.RtId);
        var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role), [],
            [AssociationFrom(missingRtId, validAssociation)]), missingRtId);

        AssertNotFound(hiddenAnswer);
        Assert.Equal(hiddenAnswer, missingAnswer);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("replace")]
    public async Task OwnRecord_InvalidPayload_KeepsTheValidationError(string operation)
    {
        var ownRecord = Store(SubjectOf(WriterOwnedRead));

        var answer = await AnswerOfAsync(() => ApplyAsync(Repository(PolicyTable()), UserSession(WriterOwnedRead),
            [Change(operation, ownRecord.RtId, false)]), ownRecord.RtId);

        AssertValidationError(answer, MandatoryTitleMessageNumberOf(operation));
    }

    [Theory]
    [InlineData("update")]
    [InlineData("replace")]
    public async Task FullReadCaller_InvalidPayload_ValidationErrorForExistingRecord_NotFoundForMissingRecord(
        string operation)
    {
        var repository = Repository(PolicyTable());
        var foreignRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var existingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(Full),
            [Change(operation, foreignRecord.RtId, false)]), foreignRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(Full),
            [Change(operation, missingRtId, false)]), missingRtId);

        AssertValidationError(existingAnswer, MandatoryTitleMessageNumberOf(operation));
        AssertNotFound(missingAnswer);
    }

    [Theory]
    [InlineData(WriterOwnedRead)]
    [InlineData(WriteOnly)]
    [InlineData(Full)]
    public async Task Insert_InvalidPayload_KeepsTheValidationError(string role)
    {
        var newRtId = OctoObjectId.GenerateNewId();

        var answer = await AnswerOfAsync(() => ApplyAsync(Repository(PolicyTable()), UserSession(role),
            [EntityUpdateInfo<RtEntity>.CreateInsert(Payload(newRtId, false))]), newRtId);

        AssertValidationError(answer, MandatoryAttributeMissingMessageNumber);
    }
}
