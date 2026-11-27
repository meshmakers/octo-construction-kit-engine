using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
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
///     The released-document lock in the write funnel (<see cref="BulkRtMutation" />), one test per abuse case of the
///     invariant: a document whose state is one of the <c>ReleasedValues</c> of its number range never changes an
///     attribute, its number, its released state or an outbound association and is never deleted, whatever the options
///     and the identity; only the <c>ChangeableAttributes</c> may change. A refused batch is refused as a whole and
///     nothing of it is written. Drafts and ranges without a release configuration behave as before.
/// </summary>
public class ReleasedDocumentLockTests
{
    private readonly ReleasedDocumentTestModel _model = new();
    private readonly ScriptedGraphRuleEngine _graph = new();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly BulkRtMutation _mutation;

    public ReleasedDocumentLockTests()
    {
        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache), _graph, [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        _model.StoreRange(changeable: ["PaymentStatus"]);
    }

    private RtEntityId Id(RtEntity entity) => new(entity.GetRtCkTypeId(), entity.RtId);

    private Task ApplyAsync(params IEntityUpdateInfo<RtEntity>[] updates) =>
        ApplyAsync(BulkRtMutationOptions.Default, _session, updates);

    private Task ApplyAsync(BulkRtMutationOptions options, IOctoSession session,
        params IEntityUpdateInfo<RtEntity>[] updates) =>
        _mutation.ApplyChangesAsync(session, _model.DataSource, _model.Cache, updates, [], options);

    private async Task<ReleasedDocumentLockedException> RefusedAsync(Func<Task> write, RtEntity released,
        Dictionary<string, object?> before)
    {
        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(write);
        Assert.Equal(0, _model.Writes);
        Assert.Equal(before, released.Attributes);
        var message = Assert.Single(refused.OperationResult.Messages);
        Assert.Equal(168, message.MessageNumber);
        Assert.Contains(released.RtId.ToString(), message.MessageText);
        return refused;
    }

    // Abuse case 1
    [Fact]
    public async Task Replace_OfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var before = _model.Snapshot(released);
        var replacement = _model.NewInvoice(ReleasedDocumentTestModel.Released, "changed", released.RtId);
        replacement.SetAttributeRawValue("Number", "RE-2026-00007");

        var refused = await RefusedAsync(
            () => ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(Id(released), replacement)), released, before);

        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal(ReleasedDocumentLock.OperationReplace, refusal.Operation);
        Assert.Equal(["Title"], refusal.Changes);
        Assert.Equal("Released", refusal.State);
        Assert.Equal(ReleasedDocumentTestModel.Range, refusal.NumberRange);
    }

    // Abuse case 2
    [Fact]
    public async Task PartialUpdate_OfABusinessAttribute_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var before = _model.Snapshot(released);
        var update = _model.Partial(released, ("Amount", 99.0));

        var refused = await RefusedAsync(
            () => ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), update)), released, before);

        Assert.Equal(["Amount"], Assert.Single(refused.Refusals).Changes);
    }

    // Abuse case 3
    [Fact]
    public async Task ChangeOfTheNumber_IsRefused_EvenWhenTheRangeListsItAsChangeable()
    {
        var model = new ReleasedDocumentTestModel();
        model.StoreRange(changeable: ["PaymentStatus", "Number"]);
        var mutation = new BulkRtMutation(new EntityRuleEngine(model.Cache), _graph, [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        var released = model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var update = model.Partial(released, ("Number", "RE-2026-00001"));

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => mutation.ApplyChangesAsync(
            _session, model.DataSource, model.Cache, [EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), update)],
            [], BulkRtMutationOptions.Default));

        Assert.Equal(["Number"], Assert.Single(refused.Refusals).Changes);
        Assert.Equal(0, model.Writes);
        Assert.Equal("RE-2026-00007", released.GetAttributeValueOrDefault("Number"));
    }

    // Abuse case 4
    [Theory]
    [InlineData(ReleasedDocumentTestModel.Draft)]
    [InlineData("Draft")]
    [InlineData(null)]
    public async Task StateBackOutOfTheReleasedValues_IsRefused(object? state)
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var before = _model.Snapshot(released);
        var update = _model.Partial(released, ("DocumentState", state));

        var refused = await RefusedAsync(
            () => ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), update)), released, before);

        Assert.Equal(["DocumentState"], Assert.Single(refused.Refusals).Changes);
    }

    [Fact]
    public async Task StateBackToDraft_IsRefused_EvenWhenTheStateIsChangeable()
    {
        var model = new ReleasedDocumentTestModel();
        model.StoreRange(changeable: ["DocumentState"]);
        var mutation = new BulkRtMutation(new EntityRuleEngine(model.Cache), _graph, [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        var released = model.StoreInvoice(ReleasedDocumentTestModel.Released);

        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => mutation.ApplyChangesAsync(_session,
            model.DataSource, model.Cache,
            [EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released),
                model.Partial(released, ("DocumentState", ReleasedDocumentTestModel.Draft)))],
            [], BulkRtMutationOptions.Default));
        Assert.Equal(0, model.Writes);

        // From one released value to another one is a change of a changeable attribute: written.
        await mutation.ApplyChangesAsync(_session, model.DataSource, model.Cache,
            [EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released),
                model.Partial(released, ("DocumentState", ReleasedDocumentTestModel.Cancelled)))],
            [], BulkRtMutationOptions.Default);
        Assert.Equal(1, model.Writes);
    }

    // Abuse case 5
    [Theory]
    [InlineData(DeleteStrategies.Archive)]
    [InlineData(DeleteStrategies.Erase)]
    public async Task Delete_OfAReleasedDocument_IsRefused(DeleteStrategies strategy)
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var before = _model.Snapshot(released);
        var options = BulkRtMutationOptions.Default with { DeleteStrategy = strategy };

        var refused = await RefusedAsync(
            () => ApplyAsync(options, _session, EntityUpdateInfo<RtEntity>.CreateDelete(Id(released))), released,
            before);

        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal(ReleasedDocumentLock.OperationDelete, refusal.Operation);
    }

    [Fact]
    public async Task UpdateThatArchives_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var before = _model.Snapshot(released);
        var update = _model.Partial(released);
        update.RtState = RtState.Archived;
        update.RtArchivedDateTime = DateTime.UtcNow;

        var refused = await RefusedAsync(
            () => ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), update)), released, before);

        Assert.Equal([ReleasedDocumentLock.RtStateChange], Assert.Single(refused.Refusals).Changes);
    }

    // Abuse case 6 (D3)
    [Fact]
    public async Task MixedBatch_IsRefusedAsAWhole_AndNothingIsWritten()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);
        var note = _model.StoreNote();
        var before = _model.Snapshot(released);

        await RefusedAsync(() => ApplyAsync(
                EntityUpdateInfo<RtEntity>.CreateUpdate(Id(draft), _model.Partial(draft, ("Title", "draft edit"))),
                EntityUpdateInfo<RtEntity>.CreateUpdate(Id(note), _model.Partial(note, ("Title", "note edit"))),
                EntityUpdateInfo<RtEntity>.CreateInsert(_model.NewInvoice(ReleasedDocumentTestModel.Draft)),
                EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), _model.Partial(released, ("Title", "x"))),
                EntityUpdateInfo<RtEntity>.CreateDelete(Id(_model.StoreNote()))),
            released, before);
    }

    // Abuse case 7
    [Fact]
    public async Task AssociationChange_OfAReleasedDocument_IsRefused_InBothDirectionsOfTheChange()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var note = _model.StoreNote();
        var before = _model.Snapshot(released);

        _graph.ToCreate.Add(_model.Association(released, note));
        var added = await RefusedAsync(() => ApplyAsync(), released, before);
        Assert.Equal(ReleasedDocumentLock.OperationAssociation, Assert.Single(added.Refusals).Operation);

        _graph.ToCreate.Clear();
        _graph.ToDelete.Add(_model.Association(released, note));
        var removed = await RefusedAsync(() => ApplyAsync(), released, before);
        Assert.Contains("Doc/Customer", Assert.Single(Assert.Single(removed.Refusals).Changes));
    }

    [Fact]
    public async Task DeleteOfAnEntityAReleasedDocumentPointsTo_IsRefused_ByTheCascadeOfItsAssociation()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var customer = _model.StoreNote("customer");
        _graph.ToDelete.Add(_model.Association(released, customer));

        await RefusedAsync(() => ApplyAsync(EntityUpdateInfo<RtEntity>.CreateDelete(Id(customer))), released,
            _model.Snapshot(released));
    }

    [Fact]
    public async Task InboundAssociation_ToAReleasedDocument_IsWritten()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var payment = _model.StoreNote("payment");
        _graph.ToCreate.Add(_model.Association(payment, released));

        await ApplyAsync();

        A.CallTo(() => _model.Associations.InsertManyAsync(_session, A<IEnumerable<RtAssociation>>._))
            .MustHaveHappenedOnceExactly();
    }

    // Abuse case 8
    [Fact]
    public async Task UserAndSystemSessions_AreRefusedAlike()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var before = _model.Snapshot(released);
        var userSession = A.Fake<IOctoSecureSession>();
        A.CallTo(() => userSession.SecurityContext).Returns(RtSecurityContext.ForUser("user-1", ["Admin"]));
        var serviceSession = A.Fake<IOctoSecureSession>();
        A.CallTo(() => serviceSession.SecurityContext).Returns(RtSecurityContext.System);

        foreach (var session in new IOctoSession[] { _session, userSession, serviceSession })
        {
            await RefusedAsync(() => ApplyAsync(BulkRtMutationOptions.Default, session,
                    EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), _model.Partial(released, ("Title", "x")))),
                released, before);
        }
    }

    [Fact]
    public async Task DisabledPreDocumentModifications_DoNotTurnTheLockOff()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var options = BulkRtMutationOptions.Default with { DisablePreDocumentModifications = true, UseBulkMode = true };

        await RefusedAsync(() => ApplyAsync(options, _session,
                EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), _model.Partial(released, ("Title", "x")))),
            released, _model.Snapshot(released));
    }

    // Abuse case 12
    [Fact]
    public async Task ChangeableAttribute_TogetherWithABusinessAttribute_IsRefusedAsAWhole()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var update = _model.Partial(released, ("PaymentStatus", "Paid"), ("Title", "x"));

        var refused = await RefusedAsync(
            () => ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), update)), released,
            _model.Snapshot(released));

        Assert.Equal(["Title"], Assert.Single(refused.Refusals).Changes);
    }

    [Fact]
    public async Task Insert_WithTheIdOfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var copy = _model.NewInvoice(ReleasedDocumentTestModel.Released, rtId: released.RtId);
        var options = BulkRtMutationOptions.Default with
        {
            UseBulkMode = true, BulkInsertStrategy = BulkInsertStrategies.Upsert
        };

        var refused = await RefusedAsync(() => ApplyAsync(options, _session, EntityUpdateInfo<RtEntity>.CreateInsert(copy)),
            released, _model.Snapshot(released));

        Assert.Equal(ReleasedDocumentLock.OperationInsert, Assert.Single(refused.Refusals).Operation);
    }

    [Fact]
    public async Task RecordArrayItemUpdate_OfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var refused = new List<ReleasedDocumentRefusal>();
        var lockScope = new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity>
            {
                [Id(released)] = _model.Partial(released)
            }, [], [], recordArrayItemUpdates: new Dictionary<RtEntityId, IReadOnlyList<RtRecordArrayItemUpdate>>
            {
                [Id(released)] = [new RtRecordArrayItemUpdate("Tags", "Key", "a", new Dictionary<string, object?> { ["Note"] = "x" })]
            });

        var exception = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.CheckMutationAsync(_session, _model.DataSource, _model.Cache, lockScope,
                new GraphRuleEngineResult(), BulkRtMutationOptions.Default));
        refused.AddRange(exception.Refusals);

        Assert.Equal(["Tags"], Assert.Single(refused).Changes);
    }

    // Allowed
    [Fact]
    public async Task UpdateOfOnlyAChangeableAttribute_IsWritten()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released),
            _model.Partial(released, ("PaymentStatus", "Paid"))));

        Assert.Equal(1, _model.Writes);
    }

    [Fact]
    public async Task UpdateThatRepeatsTheStoredValues_WithAChangeableAttribute_IsWritten()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(released), _model.Partial(released,
            ("PaymentStatus", "Paid"), ("Title", "invoice"), ("Amount", 120.5), ("DocumentState", "Released"),
            ("Tags", new[] { "a", "b" }), ("Number", "RE-2026-00007"))));

        Assert.Equal(1, _model.Writes);
    }

    [Fact]
    public async Task ReplaceWithoutTheNumber_KeepsTheStoredNumber_AndIsWritten_WhenOnlyAChangeableAttributeChanges()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var replacement = _model.NewInvoice(ReleasedDocumentTestModel.Released, rtId: released.RtId);
        replacement.SetAttributeRawValue("PaymentStatus", "Paid");

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(Id(released), replacement));

        Assert.Equal(1, _model.Writes);
    }

    [Fact]
    public async Task ReplaceWithoutTheNumber_IsRefused_WhenNoModifierCarriesTheNumberOver()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var replacement = _model.NewInvoice(ReleasedDocumentTestModel.Released, rtId: released.RtId);
        var options = BulkRtMutationOptions.Default with { DisablePreDocumentModifications = true };

        var refused = await RefusedAsync(() => ApplyAsync(options, _session,
            EntityUpdateInfo<RtEntity>.CreateReplace(Id(released), replacement)), released, _model.Snapshot(released));

        Assert.Equal(["Number"], Assert.Single(refused.Refusals).Changes);
    }

    [Fact]
    public async Task Drafts_AreWrittenAsBefore()
    {
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);
        var other = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);
        var replacement = _model.NewInvoice(ReleasedDocumentTestModel.Draft, "replaced", other.RtId);

        await ApplyAsync(
            EntityUpdateInfo<RtEntity>.CreateUpdate(Id(draft), _model.Partial(draft, ("Title", "edited"))),
            EntityUpdateInfo<RtEntity>.CreateReplace(Id(other), replacement),
            EntityUpdateInfo<RtEntity>.CreateInsert(_model.NewInvoice(ReleasedDocumentTestModel.Draft)));
        await ApplyAsync(BulkRtMutationOptions.Default with { DeleteStrategy = DeleteStrategies.Erase }, _session,
            EntityUpdateInfo<RtEntity>.CreateDelete(Id(draft)));

        Assert.True(_model.Writes >= 4, $"{_model.Writes} writes");
    }

    [Fact]
    public async Task TheUpdateThatReleasesADraft_IsWritten_TogetherWithOtherAttributes()
    {
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(Id(draft),
            _model.Partial(draft, ("DocumentState", ReleasedDocumentTestModel.Released), ("Title", "final"))));

        Assert.Equal(1, _model.Writes);
    }

    [Fact]
    public async Task InsertOfANewReleasedDocument_IsWritten()
    {
        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateInsert(_model.NewInvoice(ReleasedDocumentTestModel.Released)));

        Assert.Equal(1, _model.Writes);
    }

    [Fact]
    public async Task RangeWithoutAReleaseConfiguration_LocksNothing()
    {
        var model = new ReleasedDocumentTestModel();
        model.StoreRange(withRelease: false);
        var mutation = new BulkRtMutation(new EntityRuleEngine(model.Cache), _graph, [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        var numbered = model.StoreInvoice(ReleasedDocumentTestModel.Released);

        await mutation.ApplyChangesAsync(_session, model.DataSource, model.Cache,
            [EntityUpdateInfo<RtEntity>.CreateUpdate(Id(numbered), model.Partial(numbered, ("Title", "x")))], [],
            BulkRtMutationOptions.Default);

        Assert.Equal(1, model.Writes);
    }

    [Fact]
    public async Task UnreadableReleaseConfiguration_LocksEveryStoredDocumentOfTheType()
    {
        var model = new ReleasedDocumentTestModel();
        model.StoreRange(releasedValues: []);
        var mutation = new BulkRtMutation(new EntityRuleEngine(model.Cache), _graph, [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        var draft = model.StoreInvoice(ReleasedDocumentTestModel.Draft);

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => mutation.ApplyChangesAsync(
            _session, model.DataSource, model.Cache,
            [EntityUpdateInfo<RtEntity>.CreateUpdate(Id(draft), model.Partial(draft, ("Title", "x")))], [],
            BulkRtMutationOptions.Default));

        Assert.StartsWith("unknown:", Assert.Single(refused.Refusals).State);
        Assert.Equal(0, model.Writes);
    }

    /// <summary>Returns the associations a test puts in, as if the batch created or deleted them.</summary>
    private sealed class ScriptedGraphRuleEngine : IGraphRuleEngine
    {
        public List<RtAssociation> ToCreate { get; } = [];

        public List<RtAssociation> ToDelete { get; } = [];

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(Result());

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
            IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(Result());

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(Result());

        private GraphRuleEngineResult Result()
        {
            var result = new GraphRuleEngineResult();
            result.RtAssociationsToCreate.AddRange(ToCreate);
            result.RtAssociationsToDelete.AddRange(ToDelete);
            return result;
        }
    }
}
