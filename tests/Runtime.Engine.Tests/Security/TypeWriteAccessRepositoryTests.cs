using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;
using Xunit;
using static Meshmakers.Octo.Runtime.Engine.Tests.Security.TypeWriteAccessTestModel;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#8574: the repository write funnel (<c>ApplyChangesAsync</c>, used by GraphQL mutations, REST and every
///     caller-scoped write) and the bulk import paths refuse a write of a restricted type that the caller's principal
///     kind or channel does not admit — atomically, before anything reaches the persistence layer — on tenants with and
///     without data policies. System sessions (pipelines, blueprint seeds, migrations) keep writing; unrestricted types
///     are unaffected.
/// </summary>
public class TypeWriteAccessRepositoryTests
{
    private readonly ICkCacheService _cache = A.Fake<ICkCacheService>();
    private readonly IBulkRtMutation _mutation = A.Fake<IBulkRtMutation>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();

    public TypeWriteAccessRepositoryTests()
    {
        Register(_cache, SecretTestModel.TenantId);
        A.CallTo(() => _cache.IsTenantLoaded(SecretTestModel.TenantId)).Returns(true);
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<ConstructionKit.Contracts.DependencyGraph.CkTypeGraph>._))
            .Returns(_collection);
    }

    private RuntimeRepositoryBase Repository() =>
        new SecretBypassWritePathTests.StubRepository(_cache, _dataSource, _mutation);

    private static IOctoSession Session(RtPrincipalKind kind, string? channel = null)
    {
        var context = RtSecurityContext.ForUser("caller", ["TenantManagement"], kind);
        if (channel != null)
        {
            context = context.WithWriteChannel(channel);
        }

        var session = A.Fake<IOctoSecureSession>();
        A.CallTo(() => session.SecurityContext).Returns(context);
        return session;
    }

    private static List<IEntityUpdateInfo<RtEntity>> Insert(string type) =>
        [EntityUpdateInfo<RtEntity>.CreateInsert(Rt(type), new RtEntity(Rt(type), OctoObjectId.GenerateNewId()))];

    private Task ApplyAsync(IOctoSession session, List<IEntityUpdateInfo<RtEntity>> updates,
        List<AssociationUpdateInfo>? associations = null) =>
        Repository().ApplyChangesAsync(session, updates, associations ?? [], new DeleteOptions(), new OperationResult());

    private void AssertPersisted(bool persisted)
    {
        var call = A.CallTo(() => _mutation.ApplyChangesAsync(A<IOctoSession>._, A<IRepositoryDataSource>._,
            A<ICkCacheService>._, A<IReadOnlyList<IEntityUpdateInfo<RtEntity>>>._,
            A<IReadOnlyList<AssociationUpdateInfo>>._, A<BulkRtMutationOptions>._, A<OperationResult>._));
        if (persisted)
        {
            call.MustHaveHappenedOnceExactly();
        }
        else
        {
            call.MustNotHaveHappened();
        }
    }

    [Theory]
    [InlineData(RtPrincipalKind.Client)]
    [InlineData(RtPrincipalKind.Unknown)]
    public async Task ApplyChanges_ClientOrUnknownCaller_IsRefusedWith144_AndNothingIsPersisted(RtPrincipalKind kind)
    {
        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(Session(kind), Insert(KnowledgeType)));

        Assert.Contains(ex.OperationResult!.Messages, m => m.MessageNumber == 144);
        AssertPersisted(false);
    }

    [Fact]
    public async Task ApplyChanges_InteractiveCaller_IsAllowed()
    {
        await ApplyAsync(Session(RtPrincipalKind.Interactive), Insert(KnowledgeType));

        AssertPersisted(true);
    }

    [Fact]
    public async Task ApplyChanges_ClientOnTheDeclaredChannel_IsAllowed()
    {
        await ApplyAsync(Session(RtPrincipalKind.Client, Channel), Insert(KnowledgeType));

        AssertPersisted(true);
    }

    [Fact]
    public async Task ApplyChanges_SystemSession_IsNeverRestricted()
    {
        await ApplyAsync(A.Fake<IOctoSession>(), Insert(LockedKnowledgeType));

        AssertPersisted(true);
    }

    [Fact]
    public async Task ApplyChanges_UnrestrictedType_UnknownCaller_IsUnaffected()
    {
        await ApplyAsync(Session(RtPrincipalKind.Unknown), Insert(NoteType));

        AssertPersisted(true);
    }

    [Fact]
    public async Task ApplyChanges_MixedChangeSet_IsRejectedAtomically()
    {
        var updates = Insert(NoteType);
        updates.AddRange(Insert(SpecialKnowledgeType));

        await Assert.ThrowsAsync<RuntimeRepositoryException>(() => ApplyAsync(Session(RtPrincipalKind.Client), updates));

        AssertPersisted(false);
    }

    [Fact]
    public async Task ApplyChanges_AssociationToARestrictedEntity_IsRefused()
    {
        var note = new RtEntityId(Rt(NoteType), OctoObjectId.GenerateNewId());
        var knowledge = new RtEntityId(Rt(KnowledgeType), OctoObjectId.GenerateNewId());

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() => ApplyAsync(Session(RtPrincipalKind.Client),
            [], [AssociationUpdateInfo.CreateInsert(note, knowledge, new RtCkId<CkAssociationRoleId>("Test/Related"))]));

        Assert.Contains(ex.OperationResult!.Messages, m => m.MessageNumber == 144 &&
                                                          m.Parameters["operation"] == "association change");
        AssertPersisted(false);
    }

    [Fact]
    public async Task FilterWrites_CallerScopedClientSession_AreRefusedBeforeThePersistenceLayer()
    {
        var filter = FieldFilterCriteria.Create();
        var entity = new RtEntity(Rt(KnowledgeType), OctoObjectId.GenerateNewId());
        var session = Session(RtPrincipalKind.Client);

        foreach (var write in new Func<RuntimeRepositoryBase, Task>[]
                 {
                     r => r.UpdateOneRtEntityAsync(session, filter, entity),
                     r => r.UpdateManyRtEntityAsync(session, filter, entity),
                     r => r.ReplaceOneRtEntityAsync(session, filter, entity),
                     r => r.DeleteOneRtEntityAsync(session, Rt(KnowledgeType), filter, new DeleteOptions()),
                     r => r.DeleteManyRtEntitiesAsync(session, Rt(KnowledgeType), filter, new DeleteOptions()),
                     // A filter does not know the concrete types it matches: Interactive passes Knowledge's own block
                     // but not the block of the derived LockedKnowledge, so the filter write is refused as well.
                     r => r.DeleteManyRtEntitiesAsync(Session(RtPrincipalKind.Interactive), Rt(KnowledgeType), filter,
                         new DeleteOptions())
                 })
        {
            var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() => write(Repository()));
            Assert.Contains(ex.OperationResult.Messages, m => m.MessageNumber == 144);
        }

        // The stub's persistence layer is reached (NotSupportedException) for a system session and an unrestricted type.
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Repository().DeleteManyRtEntitiesAsync(A.Fake<IOctoSession>(), Rt(KnowledgeType), filter, new DeleteOptions()));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Repository().UpdateManyRtEntityAsync(session, filter, new RtEntity(Rt(NoteType), OctoObjectId.GenerateNewId())));
    }

    [Fact]
    public async Task BulkInsert_CallerScopedClientSession_IsRefused_SystemSessionWrites()
    {
        var entity = new RtEntity(Rt(KnowledgeType), OctoObjectId.GenerateNewId());

        await Assert.ThrowsAsync<RuntimeRepositoryException>(() => Repository()
            .BulkInsertRtEntitiesAsync(Session(RtPrincipalKind.Client), [entity], new BulkOperationOptions()));
        A.CallTo(() => _collection.BulkImportAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
            A<BulkOperationOptions>._)).MustNotHaveHappened();

        await Repository().BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions());
        A.CallTo(() => _collection.BulkImportAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
            A<BulkOperationOptions>._)).MustHaveHappenedOnceExactly();
    }
}
