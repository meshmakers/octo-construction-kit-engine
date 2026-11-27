using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The entries the engine writes into the document audit trail: the document, the action, the time in UTC, the
///     caller's subject (a user's subject or a client's id; none for a system session) and the details of the action.
/// </summary>
public class DocumentAuditTrailTests
{
    private readonly ReleasedDocumentTestModel _model = new();

    [Fact]
    public void AnEntry_NamesTheDocumentTheCallerAndTheUtcTime()
    {
        var document = new RtEntityId(_model.InvoiceId, OctoObjectId.GenerateNewId());
        var localTime = new DateTime(2026, 11, 27, 11, 0, 0, DateTimeKind.Local);

        var entry = DocumentAuditTrail.CreateEntry(_model.AuditEntry, RtSecurityContext.ForUser("client-a", []),
            document, DocumentAuditTrail.ReleaseAction, localTime,
            new Dictionary<string, object?> { ["DocumentNumber"] = "RE-2026-00001", ["FiscalYear"] = null });

        Assert.True(DocumentAuditTrail.IsAuditEntryType(entry.GetRtCkTypeId()));
        Assert.Equal(document.RtId.ToString(), entry.GetAttributeValueOrDefault("DocumentRtId"));
        Assert.Equal(_model.InvoiceId.FullName, entry.GetAttributeValueOrDefault("DocumentCkTypeId"));
        Assert.Equal(DocumentAuditTrail.ReleaseAction, entry.GetAttributeValueOrDefault("Action"));
        var timestamp = Assert.IsType<DateTime>(entry.GetAttributeValueOrDefault("Timestamp"));
        Assert.Equal(DateTimeKind.Utc, timestamp.Kind);
        Assert.Equal(localTime.ToUniversalTime(), timestamp);
        Assert.Equal("client-a", entry.GetAttributeValueOrDefault("SubjectId"));
        Assert.Equal("client-a", entry.RtCreatedBy);
        Assert.Equal("RE-2026-00001", entry.GetAttributeValueOrDefault("DocumentNumber"));
        Assert.False(entry.Attributes.ContainsKey("FiscalYear"));
    }

    [Fact]
    public void AnEntryOfASystemSession_HasNoSubject()
    {
        var entry = DocumentAuditTrail.CreateEntry(_model.AuditEntry, RtSecurityContext.System,
            new RtEntityId(_model.InvoiceId, OctoObjectId.GenerateNewId()), DocumentAuditTrail.StornoAction,
            DateTime.UtcNow);

        Assert.False(entry.Attributes.ContainsKey("SubjectId"));
        Assert.Null(entry.RtCreatedBy);
    }
}
