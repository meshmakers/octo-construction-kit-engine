using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;

namespace Meshmakers.Octo.Runtime.Engine.CkModelMigrations;

/// <summary>
/// Default <see cref="ICkModelImportAuditTrail"/> implementation. Translates typed calls into
/// generic <see cref="AuditEvent"/>s and publishes them through <see cref="IAuditEventSink"/>.
/// </summary>
/// <remarks>
/// Replaces the previous <see cref="LoggingCkModelImportAuditTrail"/> default. Going through
/// the sink lets a single host registration (e.g. <c>EventRepositoryAuditEventSink</c> in
/// <c>octo-common-services</c>) route every audit-trail kind into the same destination
/// without each kind needing its own bridge class — and without each bridge ctor-injecting
/// <c>IEventRepository</c>, which would re-introduce the WI #3324 DI bootstrap cycle.
/// </remarks>
public sealed class ForwardingCkModelImportAuditTrail : ICkModelImportAuditTrail
{
    private readonly IAuditEventSink _sink;

    /// <summary>Constructor.</summary>
    public ForwardingCkModelImportAuditTrail(IAuditEventSink sink)
    {
        _sink = sink;
    }

    /// <inheritdoc />
    public Task RecordExtensibleEnumValueOverrideAsync(
        string? tenantId,
        CkModelId ckModelId,
        CkId<CkEnumId> ckEnumId,
        string ckEnumValueName,
        string extensionValueName,
        int extensionValueKey)
    {
        var message =
            $"Extension enum value '{extensionValueName}' (key: {extensionValueKey}) overrides " +
            $"CK-defined value '{ckEnumValueName}' for enum '{ckEnumId}' during import of CK model " +
            $"'{ckModelId}'. The custom extension value takes precedence over the construction kit " +
            "definition.";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Warning,
            "CkModelImport.ExtensibleEnumOverride",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["ckModelId"] = ckModelId.ToString(),
                ["ckEnumId"] = ckEnumId.ToString(),
                ["ckEnumValueName"] = ckEnumValueName,
                ["extensionValueName"] = extensionValueName,
                ["extensionValueKey"] = extensionValueKey,
            }
        });
    }

    /// <inheritdoc />
    public Task RecordWrapScalarInRecordAsync(
        string? tenantId,
        RtCkId<CkTypeId> ckTypeId,
        OctoObjectId rtId,
        string sourceAttribute,
        RtCkId<CkRecordId> targetRecordCkRecordId,
        int wrappedCount,
        string stepId)
    {
        var message =
            $"CK migration step '{stepId}' wrapped {wrappedCount} scalar entr" +
            (wrappedCount == 1 ? "y" : "ies") +
            $" of attribute '{sourceAttribute}' on entity '{ckTypeId}@{rtId}' into records of " +
            $"type '{targetRecordCkRecordId}'.";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Information,
            "CkModelMigration.WrapScalarInRecord",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["stepId"] = stepId,
                ["ckTypeId"] = ckTypeId.ToString(),
                ["rtId"] = rtId.ToString(),
                ["sourceAttribute"] = sourceAttribute,
                ["targetRecordCkRecordId"] = targetRecordCkRecordId.ToString(),
                ["wrappedCount"] = wrappedCount,
            }
        });
    }
}
