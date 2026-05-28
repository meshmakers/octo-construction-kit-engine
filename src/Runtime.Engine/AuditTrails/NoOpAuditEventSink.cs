using Meshmakers.Octo.Runtime.Contracts.AuditTrails;

namespace Meshmakers.Octo.Runtime.Engine.AuditTrails;

/// <summary>
/// <see cref="IAuditEventSink"/> implementation that drops every event. Useful for tests that
/// want to silence audit-trail noise, or for hosts that have decided audit events are not
/// relevant. Not registered as the engine default — <see cref="LoggingAuditEventSink"/> is.
/// </summary>
public sealed class NoOpAuditEventSink : IAuditEventSink
{
    /// <inheritdoc />
    public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
