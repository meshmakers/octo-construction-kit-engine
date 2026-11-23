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
///     A change by a restricted caller that addresses a stored record under a type it is not stored as (a sibling
///     type sharing the collection) answers like a change of a missing record.
/// </summary>
public class HiddenOrMissingAddressingTests : HiddenOrMissingWriteFixture
{
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

    private RtEntity StoreRecordOfOtherType(string createdBy)
    {
        RegisterRequiredAndPlainInOneSharedCollection();
        var recordOfOtherType = new RtEntity(Model.Plain.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId())
        {
            RtCreatedBy = createdBy
        };
        StoredEntities[recordOfOtherType.RtId] = recordOfOtherType;
        return recordOfOtherType;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestrictedReadCaller_RecordAddressedUnderATypeItIsNotStoredAs_AnswersExactlyLikeMissingRecord(
        bool validPayload)
    {
        var recordOfOtherType = StoreRecordOfOtherType(OtherUser);
        var repository = Repository(PolicyTable());
        var missingRtId = OctoObjectId.GenerateNewId();

        foreach (var role in new[] { WriterOwnedRead, WriteOnly })
        {
            var otherTypeAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
                [Change("update", recordOfOtherType.RtId, validPayload)]), recordOfOtherType.RtId);
            var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(role),
                [Change("update", missingRtId, validPayload)]), missingRtId);

            AssertNotFound(otherTypeAnswer);
            Assert.Equal(otherTypeAnswer, missingAnswer);
        }
    }

    [Theory]
    [InlineData(OtherUser)]
    [InlineData("user-" + OwnedWriter)]
    public async Task OwnedOnlyWriter_RecordAddressedUnderATypeItIsNotStoredAs_AnswersNotFoundAndIsNotWritten(
        string createdBy)
    {
        var recordOfOtherType = StoreRecordOfOtherType(createdBy);
        var repository = Repository(PolicyTable());
        var missingRtId = OctoObjectId.GenerateNewId();

        foreach (var operation in new[] { "update", "replace", "delete" })
        {
            var otherTypeAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(OwnedWriter),
                [Change(operation, recordOfOtherType.RtId, true)]), recordOfOtherType.RtId);
            var missingAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(OwnedWriter),
                [Change(operation, missingRtId, true)]), missingRtId);

            AssertNotFound(otherTypeAnswer);
            Assert.Equal(otherTypeAnswer, missingAnswer);
        }

        var invalidAnswer = await AnswerOfAsync(() => ApplyAsync(repository, UserSession(OwnedWriter),
            [Change("update", recordOfOtherType.RtId, false)]), recordOfOtherType.RtId);
        AssertNotFound(invalidAnswer);
        A.CallTo(() => Collection.UpdateOneAsync(A<IOctoSession>._,
                A<IEnumerable<RtEntity>>.That.Matches(entities =>
                    entities.Any(entity => entity.RtId == recordOfOtherType.RtId))))
            .MustNotHaveHappened();
        A.CallTo(() => Collection.ReplaceManyAsync(A<IOctoSession>._,
                A<IEnumerable<RtEntity>>.That.Matches(entities =>
                    entities.Any(entity => entity.RtId == recordOfOtherType.RtId))))
            .MustNotHaveHappened();
        A.CallTo(() => Collection.DeleteOneAsync(A<IOctoSession>._, recordOfOtherType.RtId)).MustNotHaveHappened();
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
