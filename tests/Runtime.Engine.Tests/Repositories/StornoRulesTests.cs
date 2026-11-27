using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The storno rules (design note D6, D7, 1.6). A credit note cancels an invoice through <c>System/Cancels</c>; the
///     invoice's range configures the credit note as its storno type. Refused: a storno of a draft (169), a second
///     released storno (170, also when a second draft storno is released later), a storno of a storno (171) and a storno
///     of another type than the configured one (172). Allowed: the first storno of a released document, a draft storno
///     next to a released one.
/// </summary>
public class StornoRulesTests
{
    private const string StornoType = "Doc/CreditNote";
    private readonly ReleasedDocumentTestModel _model = new();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();

    public StornoRulesTests()
    {
        _model.StoreRange(storno: StornoType);
        _model.StoreRange(wellKnownName: "CreditNoteNumber");
    }

    private static RtEntityId Id(RtEntity entity) => new(entity.GetRtCkTypeId(), entity.RtId);

    private RtAssociation Cancels(RtEntity storno, RtEntity original) =>
        _model.Association(storno, original, "System/Cancels");

    private Task CreateAsync(params RtAssociation[] created) =>
        ReleasedDocumentLock.CheckStornosAsync(_session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>(), created, []);

    private static async Task<int> RefusedAsync(Func<Task> check)
    {
        var refused = await Assert.ThrowsAsync<StornoRefusedException>(check);
        return Assert.Single(refused.OperationResult.Messages).MessageNumber;
    }

    [Fact]
    public async Task FirstStorno_OfAReleasedDocument_IsAllowed()
    {
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var creditNote = _model.StoreCreditNote(ReleasedDocumentTestModel.Draft);

        await CreateAsync(Cancels(creditNote, invoice));
    }

    // D7
    [Fact]
    public async Task Storno_OfADraft_IsRefused()
    {
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);
        var creditNote = _model.StoreCreditNote(ReleasedDocumentTestModel.Draft);

        Assert.Equal(169, await RefusedAsync(() => CreateAsync(Cancels(creditNote, draft))));
    }

    // D6
    [Fact]
    public async Task SecondReleasedStorno_IsRefused_ADraftNextToTheReleasedOneIsNot()
    {
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var first = _model.StoreCreditNote(ReleasedDocumentTestModel.Released);
        _model.StoredAssociations.Add(Cancels(first, invoice));

        await CreateAsync(Cancels(_model.StoreCreditNote(ReleasedDocumentTestModel.Draft), invoice));
        Assert.Equal(170, await RefusedAsync(() =>
            CreateAsync(Cancels(_model.StoreCreditNote(ReleasedDocumentTestModel.Released), invoice))));
    }

    // D6, the release of a second draft storno
    [Fact]
    public async Task ReleasingASecondDraftStorno_IsRefused()
    {
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var first = _model.StoreCreditNote(ReleasedDocumentTestModel.Released);
        var second = _model.StoreCreditNote(ReleasedDocumentTestModel.Draft);
        _model.StoredAssociations.AddRange([Cancels(first, invoice), Cancels(second, invoice)]);
        var release = _model.Partial(second, ("DocumentState", ReleasedDocumentTestModel.Released));

        Assert.Equal(170, await RefusedAsync(() => ReleasedDocumentLock.CheckStornosAsync(_session,
            _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity> { [Id(second)] = release },
                [], []), [], [])));
    }

    [Fact]
    public async Task ReleasingTheOnlyStorno_IsAllowed()
    {
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var creditNote = _model.StoreCreditNote(ReleasedDocumentTestModel.Draft);
        _model.StoredAssociations.Add(Cancels(creditNote, invoice));
        var release = _model.Partial(creditNote, ("DocumentState", ReleasedDocumentTestModel.Released));

        await ReleasedDocumentLock.CheckStornosAsync(_session, _model.DataSource, _model.Cache,
            new EntityRuleEngineResult<RtEntity>([], new Dictionary<RtEntityId, RtEntity> { [Id(creditNote)] = release },
                [], []), [], []);
    }

    [Fact]
    public async Task Storno_OfAStorno_IsRefused()
    {
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var creditNote = _model.StoreInvoice(ReleasedDocumentTestModel.Released, "an invoice that cancels another");
        _model.StoredAssociations.Add(Cancels(creditNote, invoice));

        Assert.Equal(171, await RefusedAsync(() =>
            CreateAsync(Cancels(_model.StoreCreditNote(ReleasedDocumentTestModel.Draft), creditNote))));
    }

    [Fact]
    public async Task Storno_OfAnotherTypeThanTheConfiguredOne_IsRefused()
    {
        var invoice = _model.StoreInvoice(ReleasedDocumentTestModel.Released);
        var wrongType = _model.StoreNote("not a credit note");

        Assert.Equal(172, await RefusedAsync(() => CreateAsync(Cancels(wrongType, invoice))));
    }

    [Fact]
    public async Task OtherAssociations_AreNotJudged()
    {
        var draft = _model.StoreInvoice(ReleasedDocumentTestModel.Draft);

        await CreateAsync(_model.Association(_model.StoreNote(), draft));
    }
}
