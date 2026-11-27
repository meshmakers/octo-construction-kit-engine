using FakeItEasy;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The released-document lock on the paths that bypass the write funnel (design note D10): the bulk import of
///     entities (RT import, blueprint seed, re-apply), the import of associations and the delete of associations by id.
///     An import that changes a released document is refused; one that carries its stored content is a no-op (the entity
///     is not written); a document that does not exist yet is imported as it is, with the number from the file.
/// </summary>
public class ReleasedDocumentImportLockTests
{
    private readonly ReleasedDocumentTestModel _model = new();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();

    public ReleasedDocumentImportLockTests()
    {
        _model.StoreRange(changeable: ["PaymentStatus"]);
    }

    private Task<IReadOnlyList<RtEntity>> FilterAsync(params RtEntity[] entities) =>
        ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, entities);

    private RtEntity CopyOf(RtEntity stored)
    {
        var copy = new RtEntity(stored.GetRtCkTypeId(), stored.RtId);
        foreach (var (name, value) in stored.Attributes)
        {
            copy.SetAttributeRawValue(name, value is List<string> list ? list.ToList() : value);
        }

        return copy;
    }

    // Abuse case 9
    [Fact]
    public async Task ImportThatChangesAReleasedDocument_IsRefused()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var changed = CopyOf(released);
        changed.SetAttributeRawValue("Amount", 1.0);

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => FilterAsync(changed));

        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal(ReleasedDocumentLock.OperationImport, refusal.Operation);
        Assert.Equal(["Amount"], refusal.Changes);
        Assert.Equal(168, Assert.Single(refused.OperationResult.Messages).MessageNumber);
    }

    [Fact]
    public async Task ImportWithoutTheNumber_IsRefused_BecauseItWouldClearIt()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var withoutNumber = CopyOf(released);
        withoutNumber.SetAttributeRawValue("Number", null);

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() => FilterAsync(withoutNumber));

        Assert.Equal(["Number"], Assert.Single(refused.Refusals).Changes);
    }

    // Abuse case 10, 11 (identical re-apply)
    [Fact]
    public async Task ImportOfTheStoredContent_IsANoOp_AndTheOthersAreWritten()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);
        var draftEdit = CopyOf(draft);
        draftEdit.SetAttributeRawValue("Title", "edited");
        var newReleased = _model.NewInvoice(ReleasedDocumentTestModel.Released);
        newReleased.SetAttributeRawValue("Number", "RE-2025-00042");

        var written = await FilterAsync(CopyOf(released), draftEdit, newReleased);

        Assert.Equal([draftEdit.RtId, newReleased.RtId], written.Select(e => e.RtId));
        Assert.Equal("RE-2025-00042", written[1].GetAttributeValueOrDefault("Number"));
    }

    [Fact]
    public async Task ReApplyThatOnlyStampsTheBlueprintAnew_IsANoOp()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        released.SetAttributeRawValue("RtBlueprintAppliedAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var reApplied = CopyOf(released);
        reApplied.SetAttributeRawValue("RtBlueprintAppliedAt", DateTime.UtcNow);
        reApplied.SetAttributeRawValue("RtBlueprintSource", "Doc-1.0.1");

        Assert.Empty(await FilterAsync(reApplied));
    }

    [Fact]
    public async Task ImportThatChangesOnlyAChangeableAttribute_IsWritten()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var paid = CopyOf(released);
        paid.SetAttributeRawValue("PaymentStatus", "Paid");

        var written = await FilterAsync(paid);

        Assert.Same(paid, Assert.Single(written));
    }

    [Fact]
    public async Task ImportBatch_WithOneRefusedDocument_IsRefusedAsAWhole()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var changed = CopyOf(released);
        changed.SetAttributeRawValue("Title", "changed");

        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            FilterAsync(_model.NewInvoice(ReleasedDocumentTestModel.Draft), changed));
    }

    [Fact]
    public async Task AssociationImport_DropsTheStoredOnes_AndRefusesANewOne()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var customer = _model.StoreNote("customer");
        var other = _model.StoreNote("other");
        var stored = _model.Association(released, customer);
        _model.StoredAssociations.Add(stored);
        var inbound = _model.Association(other, released);

        var written = await ReleasedDocumentLock.FilterAssociationImportAsync(_session, _model.DataSource,
            _model.Cache, [_model.Association(released, customer), inbound]);
        Assert.Same(inbound, Assert.Single(written));

        var refused = await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.FilterAssociationImportAsync(_session, _model.DataSource, _model.Cache,
                [_model.Association(released, other)]));
        Assert.Equal(ReleasedDocumentLock.OperationAssociation, Assert.Single(refused.Refusals).Operation);
    }

    [Fact]
    public async Task DeleteOfAnOutboundAssociationById_IsRefused_AnInboundOneIsNot()
    {
        var released = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var customer = _model.StoreNote("customer");
        var outbound = _model.Association(released, customer);
        var inbound = _model.Association(customer, released);
        _model.StoredAssociations.AddRange([outbound, inbound]);

        await ReleasedDocumentLock.CheckAssociationDeleteAsync(_session, _model.DataSource, _model.Cache,
            [inbound.AssociationId]);
        await Assert.ThrowsAsync<ReleasedDocumentLockedException>(() =>
            ReleasedDocumentLock.CheckAssociationDeleteAsync(_session, _model.DataSource, _model.Cache,
                [inbound.AssociationId, outbound.AssociationId]));
    }
}
