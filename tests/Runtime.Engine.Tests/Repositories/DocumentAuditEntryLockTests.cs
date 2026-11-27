using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     Decision D9: entries of the document audit trail are append-only and only the engine creates them. Update, replace,
///     delete, every insert and an import of a new or changed entry are refused (message 173) on the write funnel and the
///     bulk import, whatever the options and the identity; an import of the stored content is a no-op.
/// </summary>
public class DocumentAuditEntryLockTests
{
    private readonly ReleasedDocumentTestModel _model = new();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();

    private RtEntity StoredEntry()
    {
        var entry = new RtEntity(_model.AuditEntry.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["DocumentRtId"] = OctoObjectId.GenerateNewId().ToString() });
        _model.Store(entry);
        return entry;
    }

    private static RtEntityId Id(RtEntity entity) => new(entity.GetRtCkTypeId(), entity.RtId);

    private Task CheckAsync(EntityRuleEngineResult<RtEntity> entities) =>
        ReleasedDocumentLock.CheckMutationAsync(_session, _model.DataSource, _model.Cache, entities,
            new GraphRuleEngineResult(), BulkRtMutationOptions.Default with { DisablePreDocumentModifications = true });

    private static async Task AssertRefusedAsync(Func<Task> write)
    {
        var refused = await Assert.ThrowsAsync<DocumentAuditEntryAppendOnlyException>(write);
        Assert.Equal(173, Assert.Single(refused.OperationResult.Messages).MessageNumber);
    }

    [Fact]
    public async Task Update_IsRefused()
    {
        var entry = StoredEntry();
        var update = new RtEntity(entry.GetRtCkTypeId(), entry.RtId,
            new Dictionary<string, object?> { ["DocumentRtId"] = "forged" });

        await AssertRefusedAsync(() => CheckAsync(new EntityRuleEngineResult<RtEntity>([],
            new Dictionary<RtEntityId, RtEntity> { [Id(entry)] = update }, [], [])));
    }

    [Fact]
    public async Task Replace_IsRefused()
    {
        var entry = StoredEntry();

        await AssertRefusedAsync(() => CheckAsync(new EntityRuleEngineResult<RtEntity>([], [],
            new Dictionary<RtEntityId, RtEntity> { [Id(entry)] = entry }, [])));
    }

    [Fact]
    public async Task Delete_IsRefused()
    {
        var entry = StoredEntry();

        await AssertRefusedAsync(() => CheckAsync(new EntityRuleEngineResult<RtEntity>([], [], [], [Id(entry)])));
    }

    [Fact]
    public async Task InsertOverAStoredEntry_IsRefused()
    {
        var entry = StoredEntry();
        var copy = new RtEntity(entry.GetRtCkTypeId(), entry.RtId, entry.Attributes);

        await AssertRefusedAsync(() => CheckAsync(new EntityRuleEngineResult<RtEntity>([copy], [], [], [])));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InsertOfANewEntry_IsRefused_WithAndWithoutAnId(bool withId)
    {
        var entry = new RtEntity(_model.AuditEntry.CkTypeId.ToRtCkId(),
            withId ? OctoObjectId.GenerateNewId() : OctoObjectId.Empty,
            new Dictionary<string, object?> { ["DocumentRtId"] = OctoObjectId.GenerateNewId().ToString() });

        await AssertRefusedAsync(() => CheckAsync(new EntityRuleEngineResult<RtEntity>([entry], [], [], [])));
    }

    [Fact]
    public async Task InsertOfANewEntry_IsRefused_AlsoForASystemSession()
    {
        // A plain session (not an IOctoSecureSession) acts as the system context.
        var systemSession = A.Fake<IOctoSession>();
        Assert.True(systemSession.GetSecurityContext().IsSystem);
        var entry = new RtEntity(_model.AuditEntry.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["DocumentRtId"] = OctoObjectId.GenerateNewId().ToString() });

        await AssertRefusedAsync(() => ReleasedDocumentLock.CheckMutationAsync(systemSession, _model.DataSource,
            _model.Cache, new EntityRuleEngineResult<RtEntity>([entry], [], [], []), new GraphRuleEngineResult(),
            BulkRtMutationOptions.Default));
    }

    [Fact]
    public async Task Import_OfTheStoredContentIsANoOp_AChangeAndANewEntryAreRefused()
    {
        var entry = StoredEntry();
        var same = new RtEntity(entry.GetRtCkTypeId(), entry.RtId, entry.Attributes);
        var changed = new RtEntity(entry.GetRtCkTypeId(), entry.RtId,
            new Dictionary<string, object?> { ["DocumentRtId"] = "forged" });
        var created = new RtEntity(entry.GetRtCkTypeId(), OctoObjectId.GenerateNewId(), entry.Attributes);

        Assert.Empty(await ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [same]));
        await AssertRefusedAsync(() =>
            ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [changed]));
        await AssertRefusedAsync(() =>
            ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [created]));
    }
}
