using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.AuditTrails;

/// <summary>
/// Default <see cref="IAuditEventSink"/> implementation that writes audit events to
/// <see cref="ILogger{TCategoryName}"/> as structured log entries. Registered as the engine's
/// default so services that don't pull in <c>octo-common-services</c>' notification stack
/// still get audit events surfaced — just in the application log instead of the platform
/// event log.
/// </summary>
public sealed class LoggingAuditEventSink : IAuditEventSink
{
    private readonly ILogger<LoggingAuditEventSink> _logger;

    /// <summary>Constructor.</summary>
    public LoggingAuditEventSink(ILogger<LoggingAuditEventSink> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var level = auditEvent.Level switch
        {
            AuditEventLevel.Information => LogLevel.Information,
            AuditEventLevel.Warning => LogLevel.Warning,
            AuditEventLevel.Error => LogLevel.Error,
            AuditEventLevel.Critical => LogLevel.Critical,
            _ => LogLevel.Information,
        };

        // Use the audit category as the first scoped value so structured-log consumers can
        // pivot on it. Tenant defaults to "<system>" so a single grep finds both system and
        // tenant-scoped entries.
        _logger.Log(level,
            "[Audit:{AuditCategory}] tenant={TenantId} {Message}",
            auditEvent.Category, auditEvent.TenantId ?? "<system>", auditEvent.Message);
        return Task.CompletedTask;
    }
}
