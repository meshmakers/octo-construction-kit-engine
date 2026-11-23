using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     The other ways to address a record answer a hidden record like a missing one too: a delete by an abstract base
///     type, and a change that addresses a stored record under a type it is not stored as (a sibling type sharing the
///     collection).
/// </summary>
public class HiddenOrMissingAddressingTests : HiddenOrMissingWriteFixture
{
    private const string AbstractBaseName = "RequiredBase";

    private RtCkId<CkTypeId> RegisterAbstractBaseOfRequired()
    {
        var baseCkTypeId = new CkId<CkTypeId>($"{Model.Required.CkTypeId.ModelId}/{AbstractBaseName}");
        var baseGraph = new CkTypeGraph(baseCkTypeId, isAbstract: true, isFinal: false, isCollectionRoot: true,
            baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null,
            derivedTypes: [new CkGraphTypeInheritance(Model.Required.CkTypeId, baseCkTypeId, 0)],
            definedAttributes: [], allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(),
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: AbstractBaseName,
            enableChangeStreamPreAndPostImages: false);
        var baseRtCkTypeId = baseCkTypeId.ToRtCkId();
        var baseGraphOut = (CkTypeGraph?)baseGraph;
        A.CallTo(() => Model.Cache.GetRtCkType(SecretTestModel.TenantId,
            A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == baseRtCkTypeId.FullName))).Returns(baseGraph);
        A.CallTo(() => Model.Cache.TryGetRtCkType(SecretTestModel.TenantId,
                A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == baseRtCkTypeId.FullName), out baseGraphOut))
            .Returns(true).AssignsOutAndRefParameters(baseGraph);
        return baseRtCkTypeId;
    }

    private void RegisterRequiredAndPlainInOneSharedCollection()
    {
        var rootCkTypeId = new CkId<CkTypeId>($"{Model.Required.CkTypeId.ModelId}/SharedRoot");
        var rootGraph = new CkTypeGraph(rootCkTypeId, isAbstract: true, isFinal: false, isCollectionRoot: true,
            baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: rootCkTypeId,
            derivedTypes:
            [
                new CkGraphTypeInheritance(Model.Required.CkTypeId, rootCkTypeId, 0),
                new CkGraphTypeInheritance(Model.Plain.CkTypeId, rootCkTypeId, 0)
            ],
            definedAttributes: [], allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(),
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "SharedRoot",
            enableChangeStreamPreAndPostImages: false);
        RegisterByCkId(rootGraph);
        RegisterInSharedCollection(Model.Required, rootCkTypeId);
        RegisterInSharedCollection(Model.Plain, rootCkTypeId);
    }

    private void RegisterInSharedCollection(CkTypeGraph storedTypeGraph, CkId<CkTypeId> rootCkTypeId)
    {
        var sharedGraph = new CkTypeGraph(storedTypeGraph.CkTypeId, isAbstract: false, isFinal: false,
            isCollectionRoot: false, baseTypes: [], derivedFromCkTypeId: rootCkTypeId,
            definingCollectionRootCkTypeId: rootCkTypeId, derivedTypes: [], definedAttributes: [],
            allAttributes: storedTypeGraph.AllAttributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: storedTypeGraph.Description ?? "shared",
            enableChangeStreamPreAndPostImages: false);
        var rtCkTypeId = storedTypeGraph.CkTypeId.ToRtCkId();
        var sharedGraphOut = (CkTypeGraph?)sharedGraph;
        A.CallTo(() => Model.Cache.GetRtCkType(SecretTestModel.TenantId,
            A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == rtCkTypeId.FullName))).Returns(sharedGraph);
        A.CallTo(() => Model.Cache.TryGetRtCkType(SecretTestModel.TenantId,
                A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == rtCkTypeId.FullName), out sharedGraphOut))
            .Returns(true).AssignsOutAndRefParameters(sharedGraph);
        RegisterByCkId(sharedGraph);
    }

    private void RegisterByCkId(CkTypeGraph typeGraph)
    {
        var typeGraphOut = (CkTypeGraph?)typeGraph;
        A.CallTo(() => Model.Cache.TryGetCkType(SecretTestModel.TenantId,
                A<CkId<CkTypeId>>.That.Matches(id => id.FullName == typeGraph.CkTypeId.FullName), out typeGraphOut))
            .Returns(true).AssignsOutAndRefParameters(typeGraph);
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
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordAddressedUnderATypeItIsNotStoredAs_AnswersExactlyLikeMissingRecord(bool validPayload)
    {
        RegisterRequiredAndPlainInOneSharedCollection();
        var repository = Repository(PolicyTable());
        var recordOfOtherType = new RtEntity(Model.Plain.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId())
        {
            RtCreatedBy = OtherUser
        };
        StoredEntities[recordOfOtherType.RtId] = recordOfOtherType;
        var missingRtId = OctoObjectId.GenerateNewId();

        foreach (var role in new[] { WriterOwnedRead, WriteOnly, Full, OwnedWriter })
        {
            var otherTypeAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
                [Change("update", recordOfOtherType.RtId, validPayload)]), recordOfOtherType.RtId);
            var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
                [Change("update", missingRtId, validPayload)]), missingRtId);

            AssertNotFound(otherTypeAnswer);
            Assert.Equal(otherTypeAnswer, missingAnswer);
        }
    }

    [Fact]
    public async Task RecordStoredAsTheAddressedType_IsWritten()
    {
        var storedRecord = Store(OtherUser);

        await ApplyAsync(Repository(RtDataPolicyTable.Empty), SystemSession(),
            [Change("update", storedRecord.RtId, true)]);

        A.CallTo(() => Collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
    }
}
