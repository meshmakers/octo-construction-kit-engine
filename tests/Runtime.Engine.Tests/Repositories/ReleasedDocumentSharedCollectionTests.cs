using Meshmakers.Octo.ConstructionKit.Contracts;
using FakeItEasy;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     MongoDB stores a whole inheritance tree in one collection and its writes match on the id alone, so a write that
///     names a sibling type reaches the stored document all the same. The lock judges the stored type: an update,
///     delete, import or association change that names the unnumbered sibling Offer with the id of a released Invoice is
///     refused like one that names Invoice.
/// </summary>
public class ReleasedDocumentSharedCollectionTests
{
    private readonly ReleasedDocumentTestModel _model = new(sharedCollection: true);
    private readonly IOctoSession _session = A.Fake<IOctoSession>();

    public ReleasedDocumentSharedCollectionTests()
    {
        _model.StoreRange(changeable: ["PaymentStatus"]);
    }

    private RtEntityId AsOffer(RtEntity entity) => new(_model.OfferId, entity.RtId);

    private Task CheckAsync(EntityRuleEngineResult<RtEntity> entities, GraphRuleEngineResult? associations = null) =>
        ReleasedDocumentLock.CheckMutationAsync(_session, _model.DataSource, _model.Cache, entities,
            associations ?? new GraphRuleEngineResult(), BulkRtMutationOptions.Default);

    [Fact]
    public async Task UpdateNamingASiblingType_OfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var update = new RtEntity(_model.OfferId, released.RtId,
            new Dictionary<string, object?> { ["Title"] = "changed" });

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => CheckAsync(
            new EntityRuleEngineResult<RtEntity>([],
                new Dictionary<RtEntityId, RtEntity> { [AsOffer(released)] = update }, [], [])));

        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal(_model.InvoiceId.FullName, refusal.DocumentId.CkTypeId.FullName);
        Assert.Equal(["Title"], refusal.Changes);
    }

    [Fact]
    public async Task DeleteNamingASiblingType_OfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => CheckAsync(
            new EntityRuleEngineResult<RtEntity>([], [], [], [AsOffer(released)])));

        Assert.Equal([ReleasedDocumentLock.RtStateChange], Assert.Single(refused.Refusals).Changes);
    }

    [Fact]
    public async Task ImportNamingASiblingType_OfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var imported = new RtEntity(_model.OfferId, released.RtId,
            new Dictionary<string, object?> { ["Title"] = "changed" });

        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [imported]));
    }

    [Fact]
    public async Task AssociationNamingASiblingOrigin_OfAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var note = _model.StoreNote();
        var association = _model.Association(released, note);
        association.OriginCkTypeId = _model.OfferId;
        var associations = new GraphRuleEngineResult();
        associations.RtAssociationsToCreate.Add(association);

        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            CheckAsync(new EntityRuleEngineResult<RtEntity>([], [], [], []), associations));
    }

    [Fact]
    public async Task WritesOfTheSiblingItself_AndOfADraft_Pass()
    {
        var offer = new RtEntity(_model.OfferId, OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["Title"] = "offer" });
        _model.Store(offer);
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);

        await CheckAsync(new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity>
        {
            [AsOffer(offer)] = new(_model.OfferId, offer.RtId, new Dictionary<string, object?> { ["Title"] = "x" }),
            [AsOffer(draft)] = new(_model.OfferId, draft.RtId, new Dictionary<string, object?> { ["Title"] = "y" })
        }, [], []));
    }
}
