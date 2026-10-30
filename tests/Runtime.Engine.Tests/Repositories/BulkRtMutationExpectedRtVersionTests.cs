using System.Linq.Expressions;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#7769: an update or replace that carries an expected <see cref="RtEntity.RtVersion" /> is written through the
///     versioned collection methods; a version mismatch is a <see cref="RtVersionConflictException" />, never a silent
///     skip. Writes without an expected version keep their paths.
/// </summary>
public class BulkRtMutationExpectedRtVersionTests
{
    private readonly SecretTestModel _model = new();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly BulkRtMutation _mutation;

    public BulkRtMutationExpectedRtVersionTests()
    {
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(_collection);
        A.CallTo(() => _collection.UpdateOneIfVersionMatchesAsync(A<IOctoSession>._, A<RtEntity>._, A<ulong>._))
            .Returns(true);
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(A<IOctoSession>._, A<OctoObjectId>._,
            A<RtEntity>._, A<ulong>._)).Returns(true);

        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache),
            new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
    }

    private RtEntityId IdOf(RtEntity entity) => new(_model.Document.CkTypeId.ToRtCkId(), entity.RtId);

    private RtEntity NewEntity(ulong rtVersion)
    {
        var entity = _model.NewDocument();
        entity.SetAttributeRawValue("Name", "changed");
        entity.RtVersion = rtVersion;
        return entity;
    }

    private Task ApplyAsync(params IEntityUpdateInfo<RtEntity>[] updates)
    {
        return _mutation.ApplyChangesAsync(_session, _dataSource, _model.Cache, updates, [],
            BulkRtMutationOptions.Default);
    }

    private void StoredVersionIs(RtEntity entity, ulong storedVersion)
    {
        var stored = _model.NewDocument(entity.RtId);
        stored.RtVersion = storedVersion;
        A.CallTo(() => _collection.FindManyAsync(A<IOctoSession>._, A<Expression<Func<RtEntity, bool>>>._))
            .Returns(Task.FromResult<ICollection<RtEntity>>([stored]));
    }

    [Fact]
    public void CreateUpdate_WithExpectedVersion_CarriesItAndLeavesTheOtherInfosUntouched()
    {
        var entity = NewEntity(3);

        var versioned = EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, 3UL);
        var plain = EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity);

        Assert.Equal(3UL, versioned.ExpectedRtVersion);
        Assert.Equal(EntityModOptions.Update, versioned.ModOption);
        Assert.Null(plain.ExpectedRtVersion);
        Assert.Null(EntityUpdateInfo<RtEntity>.CreateInsert(entity).ExpectedRtVersion);
    }

    [Fact]
    public async Task RuleEngine_ExpectedVersion_FlowsToResultForUpdateAndReplaceOnly()
    {
        var updated = NewEntity(1);
        var replaced = NewEntity(2);
        var plain = NewEntity(0);
        var engine = new EntityRuleEngine(_model.Cache);

        var result = await engine.ValidateAsync(SecretTestModel.TenantId,
            new List<IEntityUpdateInfo<RtEntity>>
            {
                EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(updated), updated, 1UL),
                EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(replaced), replaced, 2UL),
                EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(plain), plain)
            }, A.Fake<IOriginFileResolver>(), new OperationResult());

        Assert.Equal(2, result.ExpectedRtVersions.Count);
        Assert.Equal(1UL, result.ExpectedRtVersions[IdOf(updated)]);
        Assert.Equal(2UL, result.ExpectedRtVersions[IdOf(replaced)]);
        Assert.False(result.ExpectedRtVersions.ContainsKey(IdOf(plain)));
    }

    [Fact]
    public async Task Update_WithExpectedVersion_WritesThroughTheVersionedMethodOnly()
    {
        var entity = NewEntity(4);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, 4UL));

        A.CallTo(() => _collection.UpdateOneIfVersionMatchesAsync(_session, entity, 4UL))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Update_VersionMismatch_ThrowsConflictWithEntityAndExpectedVersion()
    {
        var entity = NewEntity(4);
        A.CallTo(() => _collection.UpdateOneIfVersionMatchesAsync(A<IOctoSession>._, A<RtEntity>._, A<ulong>._))
            .Returns(false);

        var ex = await Assert.ThrowsAsync<RtVersionConflictException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, 4UL)));

        Assert.Equal(IdOf(entity), ex.RtEntityId);
        Assert.Equal(4UL, ex.ExpectedRtVersion);
    }

    [Fact]
    public async Task Update_WithoutExpectedVersion_KeepsTheUnguardedBatchPath()
    {
        var entity = NewEntity(0);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity));

        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _collection.UpdateOneIfVersionMatchesAsync(A<IOctoSession>._, A<RtEntity>._, A<ulong>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Replace_WithExpectedVersion_WritesTheNextVersionThroughTheVersionedMethod()
    {
        var entity = NewEntity(7);
        StoredVersionIs(entity, 7);
        ulong? versionAtWrite = null;
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(A<IOctoSession>._, A<OctoObjectId>._,
                A<RtEntity>._, A<ulong>._))
            .Invokes(call => versionAtWrite = call.GetArgument<RtEntity>(2)!.RtVersion)
            .Returns(true);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity, 7UL));

        Assert.Equal(8UL, versionAtWrite);
        Assert.Equal(8UL, entity.RtVersion);
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(_session, entity.RtId, entity, 7UL))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Replace_VersionedAndPlainInOneBatch_OnlyThePlainOneGoesThroughReplaceMany()
    {
        var versioned = NewEntity(1);
        var plain = NewEntity(0);
        StoredVersionIs(versioned, 1);
        IReadOnlyList<OctoObjectId>? replacedMany = null;
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => replacedMany = call.GetArgument<IEnumerable<RtEntity>>(1)!.Select(e => e.RtId).ToList());

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(versioned), versioned, 1UL),
            EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(plain), plain));

        Assert.Equal([plain.RtId], replacedMany);
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(_session, versioned.RtId, versioned, 1UL))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Replace_StoredVersionDiffers_ThrowsBeforeAnythingIsWritten()
    {
        var entity = NewEntity(7);
        StoredVersionIs(entity, 9);

        var ex = await Assert.ThrowsAsync<RtVersionConflictException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity, 7UL)));

        Assert.Equal(7UL, ex.ExpectedRtVersion);
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(A<IOctoSession>._, A<OctoObjectId>._,
            A<RtEntity>._, A<ulong>._)).MustNotHaveHappened();
        A.CallTo(() => _dataSource.BinaryDataSource.DeleteAllFileSystemBinariesAsync(A<IOctoSession>._,
            A<RtEntityId>._, A<CancellationToken>._)).MustNotHaveHappened();
        Assert.Equal(7UL, entity.RtVersion);
    }

    [Fact]
    public async Task Replace_ConflictAtTheAtomicWrite_ThrowsAndRestoresTheVersionOfTheEntity()
    {
        // The stored read passed (version 7) but another writer won between read and write.
        var entity = NewEntity(7);
        StoredVersionIs(entity, 7);
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(A<IOctoSession>._, A<OctoObjectId>._,
            A<RtEntity>._, A<ulong>._)).Returns(false);

        await Assert.ThrowsAsync<RtVersionConflictException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity, 7UL)));

        Assert.Equal(7UL, entity.RtVersion);
    }

    [Fact]
    public async Task Replace_MissingEntity_IsLeftToTheCollectionNotFoundError()
    {
        // No stored entity: the pre-check passes and the versioned write reports the missing document itself.
        var entity = NewEntity(0);
        A.CallTo(() => _collection.ReplaceByIdIfVersionMatchesAsync(A<IOctoSession>._, A<OctoObjectId>._,
                A<RtEntity>._, A<ulong>._))
            .Throws(new InvalidOperationException("not found"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity, 0UL)));

        Assert.Equal(0UL, entity.RtVersion);
    }

    [Fact]
    public async Task Update_WithExpectedVersionAndLinkedBinary_IsRefusedBeforeAnyByteIsWritten()
    {
        var entity = _model.NewDocument();
        entity.SetAttributeRawValue("File", new EntityBinaryInfo
        {
            Filename = "a.txt",
            ContentType = "text/plain",
            BinaryId = OctoObjectId.GenerateNewId(),
            Stream = new MemoryStream([1])
        });

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, 1UL)));

        Assert.Contains("expected rtVersion", ex.Message);
        A.CallTo(() => _dataSource.BinaryDataSource.ReplaceFileSystemBinaryAsync(A<IOctoSession>._,
            A<RtEntityId>._, A<OctoObjectId>._, A<string>._, A<string>._, A<Stream>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _collection.UpdateOneIfVersionMatchesAsync(A<IOctoSession>._, A<RtEntity>._, A<ulong>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public void ExpectedVersion_RoundTripsThroughJsonAndIsOmittedWhenNotSet()
    {
        var entity = NewEntity(5);
        var versioned = EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, 5UL);
        var plain = EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity);

        var versionedJson = System.Text.Json.JsonSerializer.Serialize(versioned);
        var plainJson = System.Text.Json.JsonSerializer.Serialize(plain);

        Assert.Contains("\"ExpectedRtVersion\":5", versionedJson);
        Assert.DoesNotContain("ExpectedRtVersion", plainJson);
    }
}
