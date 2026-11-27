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
///     AB#9758: partial updates pass the <see cref="IPreDocumentUpdateModification{TDocument}" /> hooks after the
///     validations and before the write. What a modification sets on a partial document (e.g. the number of a document
///     the update releases) is written in the same update statement; inserts and replaces do not reach the hook, and
///     disabled pre-document modifications skip it.
/// </summary>
public class BulkRtMutationUpdateModificationTests
{
    private readonly SecretTestModel _model = new();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly List<string> _order = [];
    private readonly List<RtEntity> _written = [];
    private readonly RecordingModification _modification;
    private readonly BulkRtMutation _mutation;

    public BulkRtMutationUpdateModificationTests()
    {
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(_collection);
        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes((IOctoSession _, IEnumerable<RtEntity> entities) =>
            {
                _order.Add("write");
                _written.AddRange(entities);
            });

        _modification = new RecordingModification(_order);
        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache),
            new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()), [new OrderValidation(_order)],
            [_modification]);
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
    public async Task Update_WritesWhatTheModificationSets_InTheSameStatement()
    {
        // The partial entity carries neither the target's RtId nor a CkTypeId: both must come from the update key.
        var entity = new RtEntity();
        entity.SetAttributeRawValue("Name", "changed");
        var target = new RtEntityId(_model.Document.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        _modification.SetDescription = "set by the modification";

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateUpdate(target, entity));

        var modified = Assert.Single(Assert.Single(_modification.Calls));
        Assert.Same(entity, modified);
        var (modifiedRtId, modifiedCkTypeId) = Assert.Single(Assert.Single(_modification.Identities));
        Assert.Equal(target.RtId, modifiedRtId);
        Assert.Equal(target.CkTypeId, modifiedCkTypeId);
        Assert.Same(_session, _modification.Session);
        Assert.Same(_dataSource, _modification.DataSource);

        var written = Assert.Single(_written);
        Assert.Same(entity, written);
        Assert.Equal("changed", written.GetAttributeValueOrDefault("Name"));
        Assert.Equal("set by the modification", written.GetAttributeValueOrDefault("Description"));
        A.CallTo(() => _collection.UpdateOneAsync(_session, A<IEnumerable<RtEntity>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Update_RunsTheModificationAfterTheValidationAndBeforeTheWrite()
    {
        var entity = Partial("changed");

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity));

        Assert.Equal(["validate", "modify", "write"], _order);
    }

    [Fact]
    public async Task InsertAndReplace_DoNotReachTheModification()
    {
        var inserted = Partial("new");
        var replaced = Partial("replaced");

        await ApplyAsync(BulkRtMutationOptions.Default, EntityUpdateInfo<RtEntity>.CreateInsert(inserted),
            EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(replaced), replaced));

        Assert.Empty(_modification.Calls);
    }

    [Fact]
    public async Task DisabledPreDocumentModifications_SkipTheModification()
    {
        var entity = Partial("changed");
        _modification.SetDescription = "must not be written";
        var options = BulkRtMutationOptions.Default with { DisablePreDocumentModifications = true };

        await ApplyAsync(options, EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity));

        Assert.Empty(_modification.Calls);
        var written = Assert.Single(_written);
        Assert.Null(written.GetAttributeValueOrDefault("Description"));
    }

    private sealed class OrderValidation(List<string> order) : IPreDocumentUpdateValidation<RtEntity>
    {
        public Task RunAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<RtEntity> partialDocuments)
        {
            order.Add("validate");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingModification(List<string> order) : IPreDocumentUpdateModification<RtEntity>
    {
        public List<IReadOnlyList<RtEntity>> Calls { get; } = [];

        public List<IReadOnlyList<(OctoObjectId RtId, RtCkId<CkTypeId>? CkTypeId)>>
            Identities { get; } = [];

        public string? SetDescription { get; set; }

        public IOctoSession? Session { get; private set; }

        public IRepositoryDataSource? DataSource { get; private set; }

        public Task ModifyUpdatesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<RtEntity> partialDocuments)
        {
            order.Add("modify");
            Session = session;
            DataSource = repositoryDataSource;
            Calls.Add(partialDocuments.ToList());
            Identities.Add(partialDocuments.Select(d => (d.RtId, d.CkTypeId)).ToList());
            if (SetDescription != null)
            {
                foreach (var document in partialDocuments)
                {
                    document.SetAttributeRawValue("Description", SetDescription);
                }
            }

            return Task.CompletedTask;
        }
    }
}
