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
///     AB#9755: partial updates pass the <see cref="IPreDocumentUpdateValidation{TDocument}" /> hooks before anything of
///     the batch is written. A refusal leaves the whole batch unwritten (also its deletes and inserts); inserts and
///     replaces do not reach the hook, and disabled pre-document modifications skip it.
/// </summary>
public class BulkRtMutationUpdateValidationTests
{
    private readonly SecretTestModel _model = new();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly RecordingValidation _validation = new();
    private readonly BulkRtMutation _mutation;

    public BulkRtMutationUpdateValidationTests()
    {
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(_collection);

        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache),
            new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()), [_validation]);
    }

    private RtEntityId IdOf(RtEntity entity) => new(_model.Document.CkTypeId.ToRtCkId(), entity.RtId);

    private RtEntity Partial(string name)
    {
        var entity = _model.NewDocument();
        entity.SetAttributeRawValue("Name", name);
        return entity;
    }

    private Task ApplyAsync(BulkRtMutationOptions options, params IEntityUpdateInfo<RtEntity>[] updates) =>
        _mutation.ApplyChangesAsync(_session, _dataSource, _model.Cache, updates, [], options);

    [Fact]
    public async Task Update_PassesThePartialEntitiesWithIdAndType_ToTheValidation()
    {
        // The partial entity carries neither the target's RtId nor a CkTypeId: both must come from the update key.
        var entity = new RtEntity();
        entity.SetAttributeRawValue("Name", "changed");
        var target = new RtEntityId(_model.Document.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        Assert.NotEqual(target.RtId, entity.RtId);

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateUpdate(target, entity));

        var validated = Assert.Single(Assert.Single(_validation.Calls));
        Assert.Same(entity, validated);
        var (validatedRtId, validatedCkTypeId) = Assert.Single(Assert.Single(_validation.Identities));
        Assert.Equal(target.RtId, validatedRtId);
        Assert.Equal(target.CkTypeId, validatedCkTypeId);
        Assert.Same(_session, _validation.Session);
        Assert.Same(_dataSource, _validation.DataSource);
        A.CallTo(() => _collection.UpdateOneAsync(_session, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RefusedUpdate_WritesNothingOfTheBatch()
    {
        _validation.Refuse = true;
        var updated = Partial("changed");
        var inserted = Partial("new");
        var deleted = _model.NewDocument();

        var exception = await Record.ExceptionAsync(() => ApplyAsync(BulkRtMutationOptions.Default,
            EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(updated), updated),
            EntityUpdateInfo<RtEntity>.CreateInsert(inserted),
            EntityUpdateInfo<RtEntity>.CreateDelete(IdOf(deleted))));

        Assert.IsType<InvalidOperationException>(exception);
        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _collection.InsertManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _collection.BulkImportAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
            A<BulkOperationOptions>._)).MustNotHaveHappened();
        A.CallTo(() => _collection.DeleteOneAsync(A<IOctoSession>._, A<OctoObjectId>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task InsertAndReplace_DoNotReachTheValidation()
    {
        var inserted = Partial("new");
        var replaced = Partial("replaced");

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateInsert(inserted),
            EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(replaced), replaced));

        Assert.Empty(_validation.Calls);
    }

    [Fact]
    public async Task DisabledPreDocumentModifications_SkipTheValidation()
    {
        _validation.Refuse = true;
        var entity = Partial("changed");
        var options = BulkRtMutationOptions.Default with { DisablePreDocumentModifications = true };

        await ApplyAsync(options, EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity));

        Assert.Empty(_validation.Calls);
        A.CallTo(() => _collection.UpdateOneAsync(_session, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
    }

    private sealed class RecordingValidation : IPreDocumentUpdateValidation<RtEntity>
    {
        public List<IReadOnlyList<RtEntity>> Calls { get; } = [];

        /// <summary>RtId and CkTypeId of every partial document as the validation received it.</summary>
        public List<IReadOnlyList<(OctoObjectId RtId, RtCkId<CkTypeId>? CkTypeId)>> Identities { get; } = [];

        public bool Refuse { get; set; }

        public IOctoSession? Session { get; private set; }

        public IRepositoryDataSource? DataSource { get; private set; }

        public Task RunAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<RtEntity> partialDocuments)
        {
            Session = session;
            DataSource = repositoryDataSource;
            if (Refuse)
            {
                throw new InvalidOperationException("refused");
            }

            Calls.Add(partialDocuments.ToList());
            Identities.Add(partialDocuments.Select(d => (d.RtId, d.CkTypeId)).ToList());
            return Task.CompletedTask;
        }
    }
}
