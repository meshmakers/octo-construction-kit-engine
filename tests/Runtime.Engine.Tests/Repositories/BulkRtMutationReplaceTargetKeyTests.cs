using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#6209: the target key of a replace decides which document is written. Its RtId and CkTypeId are in the payload
///     before the <see cref="IPreDocumentModification{TDocument}" /> pipeline runs, so a payload with an empty or another
///     RtId does not look like a new entity to it (the AutoIncrement modifier would take a new number and overwrite the
///     stored one). Inserts keep their own RtId.
/// </summary>
public class BulkRtMutationReplaceTargetKeyTests
{
    private readonly SecretTestModel _model = new();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly RecordingModification _modification = new();
    private readonly RecordingUpdateValidation _validation = new();
    private readonly BulkRtMutation _mutation;

    public BulkRtMutationReplaceTargetKeyTests()
    {
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(_collection);

        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache),
            new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [_modification],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()), [_validation]);
    }

    private RtEntityId NewTarget() => new(_model.Document.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());

    private static RtEntity Payload(OctoObjectId rtId)
    {
        // No CkTypeId, like an entity a client builds itself: the target key brings it.
        var entity = new RtEntity { RtId = rtId };
        entity.SetAttributeRawValue("Name", "replaced");
        return entity;
    }

    private Task ApplyAsync(BulkRtMutationOptions options, params IEntityUpdateInfo<RtEntity>[] updates) =>
        _mutation.ApplyChangesAsync(_session, _dataSource, _model.Cache, updates, [], options);

    [Fact]
    public async Task Replace_PayloadWithEmptyRtId_ModificationSeesTheTargetKey()
    {
        var target = NewTarget();
        var payload = Payload(OctoObjectId.Empty);
        IReadOnlyList<OctoObjectId>? replaced = null;
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => replaced = call.GetArgument<IEnumerable<RtEntity>>(1)!.Select(e => e.RtId).ToList());

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateReplace(target, payload));

        var (rtId, ckTypeId) = Assert.Single(Assert.Single(_modification.Identities));
        Assert.Equal(target.RtId, rtId);
        Assert.Equal(target.CkTypeId, ckTypeId);
        Assert.Same(payload, Assert.Single(Assert.Single(_modification.Calls)));
        Assert.Equal([target.RtId], replaced);
    }

    [Fact]
    public async Task Replace_PayloadWithAnotherRtId_ModificationSeesTheTargetKey()
    {
        var target = NewTarget();
        var payload = Payload(OctoObjectId.GenerateNewId());
        Assert.NotEqual(target.RtId, payload.RtId);
        IReadOnlyList<OctoObjectId>? replaced = null;
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => replaced = call.GetArgument<IEnumerable<RtEntity>>(1)!.Select(e => e.RtId).ToList());

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateReplace(target, payload));

        var (rtId, ckTypeId) = Assert.Single(Assert.Single(_modification.Identities));
        Assert.Equal(target.RtId, rtId);
        Assert.Equal(target.CkTypeId, ckTypeId);
        Assert.Equal([target.RtId], replaced);
    }

    [Fact]
    public async Task Replace_DisabledPreDocumentModifications_StillWritesTheTargetKey()
    {
        var target = NewTarget();
        var payload = Payload(OctoObjectId.GenerateNewId());
        IReadOnlyList<OctoObjectId>? replaced = null;
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => replaced = call.GetArgument<IEnumerable<RtEntity>>(1)!.Select(e => e.RtId).ToList());
        var options = BulkRtMutationOptions.Default with { DisablePreDocumentModifications = true };

        await ApplyAsync(options, EntityUpdateInfo<RtEntity>.CreateReplace(target, payload));

        Assert.Empty(_modification.Calls);
        Assert.Equal([target.RtId], replaced);
        Assert.Equal(target.CkTypeId, payload.CkTypeId);
    }

    [Fact]
    public async Task Insert_KeepsItsOwnRtId_ForTheModification()
    {
        var inserted = _model.NewDocument();
        inserted.SetAttributeRawValue("Name", "new");
        var ownRtId = inserted.RtId;

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateInsert(inserted));

        var (rtId, _) = Assert.Single(Assert.Single(_modification.Identities));
        Assert.Equal(ownRtId, rtId);
    }

    [Fact]
    public async Task UpdateAndReplace_InOneBatch_UpdateValidationSeesOnlyTheUpdate_ModificationOnlyTheReplace()
    {
        // AB#9755 stays intact: partial updates reach the update validation (with their key), replaces the
        // pre-document modifications (with their key).
        var updateTarget = NewTarget();
        var update = Payload(OctoObjectId.Empty);
        var replaceTarget = NewTarget();
        var replace = Payload(OctoObjectId.Empty);

        await ApplyAsync(BulkRtMutationOptions.Default,
            EntityUpdateInfo<RtEntity>.CreateUpdate(updateTarget, update),
            EntityUpdateInfo<RtEntity>.CreateReplace(replaceTarget, replace));

        var (validatedRtId, _) = Assert.Single(Assert.Single(_validation.Identities));
        Assert.Equal(updateTarget.RtId, validatedRtId);
        var (modifiedRtId, _) = Assert.Single(Assert.Single(_modification.Identities));
        Assert.Equal(replaceTarget.RtId, modifiedRtId);
    }

    private sealed class RecordingModification : IPreDocumentModification<RtEntity>
    {
        public List<IReadOnlyList<RtEntity>> Calls { get; } = [];

        /// <summary>RtId and CkTypeId of every document as the modification received it.</summary>
        public List<IReadOnlyList<(OctoObjectId RtId, RtCkId<CkTypeId>? CkTypeId)>> Identities { get; } = [];

        public Task RunAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IEnumerable<RtEntity> documents)
        {
            var list = documents.ToList();
            Calls.Add(list);
            Identities.Add(list.Select(d => (d.RtId, d.CkTypeId)).ToList());
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUpdateValidation : IPreDocumentUpdateValidation<RtEntity>
    {
        public List<IReadOnlyList<(OctoObjectId RtId, RtCkId<CkTypeId>? CkTypeId)>> Identities { get; } = [];

        public Task RunAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<RtEntity> partialDocuments)
        {
            Identities.Add(partialDocuments.Select(d => (d.RtId, d.CkTypeId)).ToList());
            return Task.CompletedTask;
        }
    }
}
