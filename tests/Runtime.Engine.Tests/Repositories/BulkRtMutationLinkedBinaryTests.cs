using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#6248: the id returned by <see cref="ILinkedBinaryDataSource.ReplaceFileSystemBinaryAsync" /> is what the
///     entity must carry. <see cref="BulkRtMutation" /> writes it into the entity before the entity write, i.e. inside
///     the same transaction - there is no step after the commit that has to fix the reference up.
/// </summary>
public class BulkRtMutationLinkedBinaryTests
{
    private readonly SecretTestModel _model = new();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly List<OctoObjectId?> _binaryIdAtWrite = [];
    private readonly BulkRtMutation _mutation;

    public BulkRtMutationLinkedBinaryTests()
    {
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph.CkTypeGraph>._))
            .Returns(_collection);
        A.CallTo(() => _collection.InsertManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => Capture(call.GetArgument<IEnumerable<RtEntity>>(1)!));
        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => Capture(call.GetArgument<IEnumerable<RtEntity>>(1)!));
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => Capture(call.GetArgument<IEnumerable<RtEntity>>(1)!));

        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache), new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
    }

    private void Capture(IEnumerable<RtEntity> entities)
    {
        // Snapshot at write time: the reference must already be final when the entity is written.
        _binaryIdAtWrite.AddRange(entities.Select(e => e.GetAttributeLinkedBinaryValueOrDefault("File")?.BinaryId));
    }

    private RtEntityId IdOf(RtEntity entity) => new(_model.Document.CkTypeId.ToRtCkId(), entity.RtId);

    private RtEntity DocumentWith(OctoObjectId? binaryId)
    {
        var entity = _model.NewDocument();
        entity.SetAttributeRawValue("File", new EntityBinaryInfo
        {
            Filename = "a.txt",
            ContentType = "text/plain",
            BinaryId = binaryId,
            Stream = new MemoryStream([1, 2, 3])
        });
        return entity;
    }

    private Task ApplyAsync(params IEntityUpdateInfo<RtEntity>[] updates)
    {
        return _mutation.ApplyChangesAsync(_session, _dataSource, _model.Cache, updates, [],
            BulkRtMutationOptions.Default);
    }

    [Fact]
    public async Task Update_WithExistingBinaryId_WritesTheIdReturnedByReplaceIntoTheEntityBeforeTheEntityWrite()
    {
        var oldId = OctoObjectId.GenerateNewId();
        var stagedId = OctoObjectId.GenerateNewId();
        A.CallTo(() => _dataSource.BinaryDataSource.ReplaceFileSystemBinaryAsync(_session, oldId, "a.txt",
                "text/plain", A<Stream>._, A<CancellationToken>._))
            .Returns(stagedId);
        var entity = DocumentWith(oldId);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity));

        Assert.Equal([stagedId], _binaryIdAtWrite);
        Assert.Equal(stagedId, entity.GetAttributeLinkedBinaryValueOrDefault("File")!.BinaryId);
    }

    [Fact]
    public async Task Replace_DeletesTheOldBytesAndStoresTheUploadIdInTheEntity()
    {
        // A full replace has always been delete + upload (new id); the deletion is deferred to the commit by the
        // data source, so the old bytes survive a rollback.
        var uploadedId = OctoObjectId.GenerateNewId();
        A.CallTo(() => _dataSource.BinaryDataSource.UploadFileSystemBinaryAsync(A<IOctoSession>._, A<RtEntityId>._,
                A<string>._, A<string>._, A<Stream>._, A<CancellationToken>._))
            .Returns(uploadedId);
        var entity = DocumentWith(OctoObjectId.GenerateNewId());

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity));

        Assert.Equal([uploadedId], _binaryIdAtWrite);
        A.CallTo(() => _dataSource.BinaryDataSource.DeleteAllFileSystemBinariesAsync(_session, IdOf(entity),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Insert_WithoutBinaryId_UsesTheUploadIdAndNeverReplaces()
    {
        var uploadedId = OctoObjectId.GenerateNewId();
        A.CallTo(() => _dataSource.BinaryDataSource.UploadFileSystemBinaryAsync(A<IOctoSession>._, A<RtEntityId>._,
                A<string>._, A<string>._, A<Stream>._, A<CancellationToken>._))
            .Returns(uploadedId);
        var entity = DocumentWith(null);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateInsert(entity));

        Assert.Equal([uploadedId], _binaryIdAtWrite);
        A.CallTo(() => _dataSource.BinaryDataSource.ReplaceFileSystemBinaryAsync(A<IOctoSession>._,
            A<OctoObjectId>._, A<string>._, A<string>._, A<Stream>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
