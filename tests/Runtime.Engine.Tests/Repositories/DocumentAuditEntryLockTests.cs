using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     Decision D9: entries of the document audit trail are append-only. Update, replace, delete and an insert or import
///     over a stored entry are refused (message 173) on the write funnel and the bulk import, whatever the options; an
///     import of the stored content is a no-op; new entries are written.
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
    public async Task InsertOverAStoredEntry_IsRefused_ANewEntryIsWritten()
    {
        var entry = StoredEntry();
        var copy = new RtEntity(entry.GetRtCkTypeId(), entry.RtId, entry.Attributes);

        await AssertRefusedAsync(() => CheckAsync(new EntityRuleEngineResult<RtEntity>([copy], [], [], [])));
        await CheckAsync(new EntityRuleEngineResult<RtEntity>(
            [new RtEntity(entry.GetRtCkTypeId(), OctoObjectId.GenerateNewId(), entry.Attributes)], [], [], []));
    }

    [Fact]
    public async Task Import_OfTheStoredContentIsANoOp_AChangeIsRefused()
    {
        var entry = StoredEntry();
        var same = new RtEntity(entry.GetRtCkTypeId(), entry.RtId, entry.Attributes);
        var changed = new RtEntity(entry.GetRtCkTypeId(), entry.RtId,
            new Dictionary<string, object?> { ["DocumentRtId"] = "forged" });

        Assert.Empty(await ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [same]));
        await AssertRefusedAsync(() =>
            ReleasedDocumentLock.FilterImportAsync(_session, _model.DataSource, _model.Cache, [changed]));
    }
}
