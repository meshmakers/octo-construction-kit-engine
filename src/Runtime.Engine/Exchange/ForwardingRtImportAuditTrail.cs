using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.Exchange;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
/// Default <see cref="IRtImportAuditTrail"/> implementation. Translates typed calls into
/// generic <see cref="AuditEvent"/>s and publishes them through <see cref="IAuditEventSink"/>,
/// following the same forwarding pattern as <c>ForwardingCkModelImportAuditTrail</c> — a single
/// host-side sink replacement routes every audit-trail kind without per-kind bridge classes
/// (and without re-introducing the WI #3324 DI bootstrap cycle).
/// </summary>
public sealed class ForwardingRtImportAuditTrail : IRtImportAuditTrail
{
    private readonly IAuditEventSink _sink;

    /// <summary>Constructor.</summary>
    public ForwardingRtImportAuditTrail(IAuditEventSink sink)
    {
        _sink = sink;
    }

    /// <inheritdoc />
    public Task RecordMissingMandatoryAttributesAsync(
        string? tenantId,
        RtCkId<CkTypeId> ckTypeId,
        OctoObjectId rtId,
        IReadOnlyList<string> missingCkAttributeIds)
    {
        var attributeList = string.Join(", ", missingCkAttributeIds.Select(a => $"'{a}'"));
        var message =
            $"Imported entity '{ckTypeId}@{rtId}' is missing mandatory attribute(s) {attributeList} " +
            "(no default value or auto-increment reference exists to fill them). The import path " +
            "bypasses entity validation, so the entity is stored in a state its CK type forbids — " +
            "typed reads and non-null GraphQL fields on these attributes will fail. Fix the import " +
            "source to supply the attribute(s).";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Warning,
            "RtImport.MissingMandatoryAttributes",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["ckTypeId"] = ckTypeId.ToString(),
                ["rtId"] = rtId.ToString(),
                ["missingCkAttributeIds"] = missingCkAttributeIds,
            }
        });
    }
}
