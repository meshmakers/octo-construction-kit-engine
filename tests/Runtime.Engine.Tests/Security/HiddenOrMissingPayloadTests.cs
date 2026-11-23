using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     For a caller with restricted read, a hidden and a missing record answer alike also where the payload itself is
///     refused: a change addressed by an abstract base type, and a replace that lacks a mandatory secret.
/// </summary>
public class HiddenOrMissingPayloadTests : HiddenOrMissingWriteFixture
{
    private const string ConfigType = "Test/Config";
    private const int CkTypeIdIsAbstractMessageNumber = 4;

    private static RtDataPolicyTable ConfigPolicyTable() =>
        new(
        [
            new RtDataPolicyRule("c1", new HashSet<string> { ConfigType }, [RtDataAction.Write, RtDataAction.Delete],
                false, false, new HashSet<string> { WriterOwnedRead, WriteOnly }),
            new RtDataPolicyRule("c2", new HashSet<string> { ConfigType }, [RtDataAction.Read], true, false,
                new HashSet<string> { WriterOwnedRead })
        ]);

    private IEntityUpdateInfo<RtEntity> ChangeByAbstractType(string operation, RtCkId<CkTypeId> abstractTypeId,
        OctoObjectId rtId)
    {
        var entityId = new RtEntityId(abstractTypeId, rtId);
        var payload = new RtEntity(abstractTypeId, rtId);
        payload.SetAttributeRawValue("Title", "changed");
        return operation switch
        {
            "update" => EntityUpdateInfo<RtEntity>.CreateUpdate(entityId, payload),
            "replace" => EntityUpdateInfo<RtEntity>.CreateReplace(entityId, payload),
            "delete" => EntityUpdateInfo<RtEntity>.CreateDelete(entityId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation")
        };
    }

    private RtEntity StoreConfig(string createdBy)
    {
        var storedConfig = new RtEntity(Model.Config.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId())
        {
            RtCreatedBy = createdBy
        };
        storedConfig.SetAttributeRawValue("Name", "stored");
        StoredEntities[storedConfig.RtId] = storedConfig;
        return storedConfig;
    }

    private IEntityUpdateInfo<RtEntity> ReplaceConfigWithoutApiKey(OctoObjectId rtId)
    {
        var configTypeId = Model.Config.CkTypeId.ToRtCkId();
        var payload = new RtEntity(configTypeId, rtId);
        payload.SetAttributeRawValue("Name", "replaced");
        return EntityUpdateInfo<RtEntity>.CreateReplace(new RtEntityId(configTypeId, rtId), payload);
    }

    public static TheoryData<string, string> RestrictedCallersAndOperations()
    {
        var data = new TheoryData<string, string>();
        foreach (var role in new[] { WriterOwnedRead, WriteOnly })
        {
            foreach (var operation in new[] { "update", "replace", "delete" })
            {
                data.Add(role, operation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RestrictedCallersAndOperations))]
    public async Task ChangeByAbstractType_HiddenRecord_AnswersExactlyLikeMissingRecord(string role, string operation)
    {
        var abstractTypeId = RegisterAbstractBaseOfRequired();
        var repository = Repository(PolicyTable());
        var hiddenRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var hiddenAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
            [ChangeByAbstractType(operation, abstractTypeId, hiddenRecord.RtId)]), hiddenRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
            [ChangeByAbstractType(operation, abstractTypeId, missingRtId)]), missingRtId);

        AssertValidationError(hiddenAnswer, CkTypeIdIsAbstractMessageNumber);
        Assert.Equal(hiddenAnswer, missingAnswer);
        Assert.True(StoredEntities.ContainsKey(hiddenRecord.RtId));
    }

    [Theory]
    [InlineData(WriterOwnedRead)]
    [InlineData(WriteOnly)]
    public async Task ReplaceWithoutMandatorySecret_HiddenRecord_AnswersExactlyLikeMissingRecord(string role)
    {
        var repository = Repository(ConfigPolicyTable());
        var hiddenConfig = StoreConfig(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var hiddenAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
            [ReplaceConfigWithoutApiKey(hiddenConfig.RtId)]), hiddenConfig.RtId);
        var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
            [ReplaceConfigWithoutApiKey(missingRtId)]), missingRtId);

        AssertNotFound(hiddenAnswer);
        Assert.Equal(hiddenAnswer, missingAnswer);
    }
}
