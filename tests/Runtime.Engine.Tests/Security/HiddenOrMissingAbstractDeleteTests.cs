using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     A delete by an abstract base type answers a hidden record, a record outside the addressed subtree and a record
///     of a write-restricted type exactly like a missing one.
/// </summary>
public class HiddenOrMissingAbstractDeleteTests : HiddenOrMissingWriteFixture
{
    private const string AbstractBaseName = "RequiredBase";
    private const int TypeWriteAccessDeniedMessageNumber = 144;

    private RtCkId<CkTypeId> RegisterAbstractBaseOfRequired()
    {
        var baseCkTypeId = new CkId<CkTypeId>($"{Model.Required.CkTypeId.ModelId}/{AbstractBaseName}");
        var baseGraph = new CkTypeGraph(baseCkTypeId, isAbstract: true, isFinal: false, isCollectionRoot: true,
            baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null,
            derivedTypes: [new CkGraphTypeInheritance(Model.Required.CkTypeId, baseCkTypeId, 0)],
            definedAttributes: [], allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(),
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: AbstractBaseName,
            enableChangeStreamPreAndPostImages: false);
        RegisterGraph(baseGraph);
        return baseCkTypeId.ToRtCkId();
    }

    private void RestrictWritesOfRequiredToClients()
    {
        var required = Model.Required;
        var restrictedRequired = new CkTypeGraph(required.CkTypeId, isAbstract: false, isFinal: false,
            isCollectionRoot: true, baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null,
            derivedTypes: [], definedAttributes: [],
            allAttributes: required.AllAttributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "Required",
            enableChangeStreamPreAndPostImages: false,
            writeAccess: new CkTypeWriteAccessDto { Principals = [CkWritePrincipals.Client] });
        RegisterGraph(restrictedRequired);
    }

    [Theory]
    [InlineData(WriterOwnedRead)]
    [InlineData(WriteOnly)]
    public async Task DeleteByAbstractType_HiddenRecord_AnswersExactlyLikeMissingRecord(string role)
    {
        var abstractTypeId = RegisterAbstractBaseOfRequired();
        var repository = Repository(PolicyTable());
        var hiddenRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var hiddenAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(UserSession(role),
            abstractTypeId, hiddenRecord.RtId, new DeleteOptions()), hiddenRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(UserSession(role),
            abstractTypeId, missingRtId, new DeleteOptions()), missingRtId);

        AssertNotFound(hiddenAnswer);
        Assert.Equal(hiddenAnswer, missingAnswer);
    }

    [Theory]
    [InlineData(WriterOwnedRead)]
    [InlineData(WriteOnly)]
    public async Task DeleteByAbstractType_HiddenRecord_BaseNotFoundMessage_NamesTheAddressedTypeLikeMissingRecord(
        string role)
    {
        var abstractTypeId = RegisterAbstractBaseOfRequired();
        var repository = RepositoryWithTheBaseNotFoundMessage(PolicyTable());
        var hiddenRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var hiddenAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(UserSession(role),
            abstractTypeId, hiddenRecord.RtId, new DeleteOptions()), hiddenRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(UserSession(role),
            abstractTypeId, missingRtId, new DeleteOptions()), missingRtId);

        var quotedAbstractTypeName = $"'{abstractTypeId.SemanticVersionedFullName}'";
        var quotedConcreteTypeName = $"'{Model.Required.CkTypeId.ToRtCkId().SemanticVersionedFullName}'";
        Assert.Equal(nameof(RuntimeRepositoryException), hiddenAnswer.ExceptionType);
        Assert.Contains(quotedAbstractTypeName, hiddenAnswer.Message);
        Assert.DoesNotContain(quotedConcreteTypeName, hiddenAnswer.Message);
        Assert.Equal(missingAnswer, hiddenAnswer);
    }

    [Fact]
    public async Task DeleteByAbstractType_MissingRecord_SystemSession_AnswersNotFound()
    {
        var abstractTypeId = RegisterAbstractBaseOfRequired();
        var missingRtId = OctoObjectId.GenerateNewId();

        var answer = await AnswerOfAsync(() => Repository(RtDataPolicyTable.Empty).DeleteOneRtEntityByRtIdAsync(
            SystemSession(), abstractTypeId, missingRtId, new DeleteOptions()), missingRtId);

        AssertNotFound(answer);
    }

    [Theory]
    [InlineData(Full)]
    [InlineData(WriterOwnedRead)]
    public async Task DeleteByAbstractType_WriteRestrictedType_ExistingRecordAnswersExactlyLikeMissingRecord(string role)
    {
        var abstractTypeId = RegisterAbstractBaseOfRequired();
        RestrictWritesOfRequiredToClients();
        var repository = RepositoryWithTheBaseNotFoundMessage(PolicyTable());
        var existingRecord = Store(OtherUser);
        var missingRtId = OctoObjectId.GenerateNewId();

        var existingAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(UserSession(role),
            abstractTypeId, existingRecord.RtId, new DeleteOptions()), existingRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(UserSession(role),
            abstractTypeId, missingRtId, new DeleteOptions()), missingRtId);

        AssertValidationError(existingAnswer, TypeWriteAccessDeniedMessageNumber);
        Assert.Equal(missingAnswer, existingAnswer);
        Assert.True(StoredEntities.ContainsKey(existingRecord.RtId));
    }

    [Fact]
    public async Task DeleteByAbstractType_RecordOutsideTheAddressedSubtree_AnswersExactlyLikeMissingRecord()
    {
        var abstractTypeId = RegisterAbstractBaseOfRequired();
        var repository = RepositoryWithTheBaseNotFoundMessage(RtDataPolicyTable.Empty);
        var siblingRecord = new RtEntity(Model.Plain.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        StoredEntities[siblingRecord.RtId] = siblingRecord;
        var missingRtId = OctoObjectId.GenerateNewId();

        var siblingAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(SystemSession(),
            abstractTypeId, siblingRecord.RtId, new DeleteOptions()), siblingRecord.RtId);
        var missingAnswer = await AnswerOfAsync(() => repository.DeleteOneRtEntityByRtIdAsync(SystemSession(),
            abstractTypeId, missingRtId, new DeleteOptions()), missingRtId);

        Assert.Equal(nameof(RuntimeRepositoryException), siblingAnswer.ExceptionType);
        Assert.Equal(missingAnswer, siblingAnswer);
        Assert.True(StoredEntities.ContainsKey(siblingRecord.RtId));
    }
}
