namespace Meshmakers.Octo.Runtime.Contracts.AuditTrails;

/// <summary>
/// Single host-side extensibility point for audit events emitted by the engine. Every typed
/// audit-trail interface in the engine (e.g. <c>ICkModelImportAuditTrail</c>,
/// <c>IArchiveAuditTrail</c>) has a default forwarding implementation in
/// <c>Runtime.Engine</c> that translates its typed calls into <see cref="AuditEvent"/>s and
/// calls <see cref="PublishAsync"/>. A host project that wants events to land somewhere other
/// than <see cref="Microsoft.Extensions.Logging.ILogger"/> registers exactly one
/// <see cref="IAuditEventSink"/> implementation — for example
/// <c>EventRepositoryAuditEventSink</c> in <c>octo-common-services</c>, which persists the
/// event to the platform event log.
/// </summary>
/// <remarks>
/// Why the indirection: the typed audit-trail interfaces are resolved during the engine's DI
/// bootstrap (they are ctor-injected into <c>IDatabaseCkModelRepository</c>, which itself is
/// resolved during <c>SystemContext</c> construction). A per-interface bridge that ctor-injects
/// <c>IEventRepository</c> (and through it <c>ISystemContext</c>) would close a DI bootstrap
/// cycle and deadlock service startup — and did, in production, the day a per-interface bridge
/// was added (WI #3324). Routing every audit-trail interface through a single
/// <see cref="IAuditEventSink"/> means a host can break the cycle exactly once, in the sink
/// implementation, by lazy-resolving the event repository via <c>IServiceProvider</c>.
/// </remarks>
public interface IAuditEventSink
{
    /// <summary>
    /// Publishes an audit event. Implementations must not throw on transient failures (the
    /// engine operations that triggered the event must still succeed even if audit-trail
    /// bookkeeping fails); recoverable errors are logged inside the sink and swallowed.
    /// </summary>
    Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
