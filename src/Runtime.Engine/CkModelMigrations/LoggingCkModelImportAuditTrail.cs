using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.CkModelMigrations;

/// <summary>
/// Default <see cref="ICkModelImportAuditTrail"/> implementation that writes structured warning
/// logs. Stand-in until a host project bridges the audit trail to the platform
/// notification/event repository (analogous to <c>LoggingArchiveAuditTrail</c>). Safe to keep
/// as the default even with a real bridge in place: structured warning logs of these
/// transitions remain useful.
/// </summary>
public sealed class LoggingCkModelImportAuditTrail : ICkModelImportAuditTrail
{
    private readonly ILogger<LoggingCkModelImportAuditTrail> _logger;

    /// <summary>Constructor.</summary>
    public LoggingCkModelImportAuditTrail(ILogger<LoggingCkModelImportAuditTrail> logger)
    {
        _logger = logger;
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
        _logger.LogWarning(
            "Extension enum value '{ExtensionValueName}' (key: {ExtensionValueKey}) overrides CK-defined value '{CkEnumValueName}' for enum '{CkEnumId}' during import of CK model '{CkModelId}' (tenant: {TenantId}). The custom extension value takes precedence over the construction kit definition.",
            extensionValueName, extensionValueKey, ckEnumValueName, ckEnumId, ckModelId,
            tenantId ?? "<system>");
        return Task.CompletedTask;
    }
}
