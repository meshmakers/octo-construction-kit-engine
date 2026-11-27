using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The <c>Denied</c> audit entries of refused writes of released documents: one per refused document with the
///     document, the time, the caller, the write channel, the attributes and the message number; none on a tenant
///     without the audit type.
/// </summary>
public class ReleasedDocumentDeniedAuditTests
{
    [Fact]
    public void OneEntryPerRefusal_WithTheCallerAndTheChanges()
    {
        var model = new ReleasedDocumentTestModel();
        var auditType = A.Fake<ICkCacheService>();
        var refusal = new ReleasedDocumentRefusal(new RtEntityId(model.InvoiceId, OctoObjectId.GenerateNewId()),
            ReleasedDocumentLock.OperationUpdate, ["Title"], "Released", ReleasedDocumentTestModel.Range);
        var now = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);

        var none = ReleasedDocumentDeniedAudit.CreateEntries(auditType, ReleasedDocumentTestModel.TenantId,
            RtSecurityContext.System, [refusal], now, out var missing);
        Assert.Null(missing);
        Assert.Empty(none);

        var entries = ReleasedDocumentDeniedAudit.CreateEntries(model.Cache, ReleasedDocumentTestModel.TenantId,
            RtSecurityContext.ForUser("user-1", []).WithWriteChannel("pipeline"), [refusal], now, out var graph);

        Assert.NotNull(graph);
        var entry = Assert.Single(entries);
        Assert.Equal(refusal.DocumentId.RtId.ToString(), entry.GetAttributeValueOrDefault("DocumentRtId"));
        Assert.Equal(model.InvoiceId.FullName, entry.GetAttributeValueOrDefault("DocumentCkTypeId"));
        Assert.Equal(ReleasedDocumentDeniedAudit.DeniedAction, entry.GetAttributeValueOrDefault("Action"));
        Assert.Equal(now, entry.GetAttributeValueOrDefault("Timestamp"));
        Assert.Equal("user-1", entry.GetAttributeValueOrDefault("SubjectId"));
        Assert.Equal("pipeline", entry.GetAttributeValueOrDefault("Source"));
        Assert.Equal(new List<string> { "Title" }, entry.GetAttributeValueOrDefault("AttributeNames"));
        Assert.StartsWith("168:", (string)entry.GetAttributeValueOrDefault("Message")!);
    }
}
