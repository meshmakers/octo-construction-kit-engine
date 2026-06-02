namespace Meshmakers.Octo.Runtime.Contracts.AuditTrails;

/// <summary>
/// Severity classification for an <see cref="AuditEvent"/>. Mirrors the platform event-store
/// levels deliberately (Information, Warning, Error, Critical) so a host-side sink that
/// persists events to the event log can map 1:1 without lossy widening or narrowing.
/// </summary>
public enum AuditEventLevel
{
    /// <summary>Routine notable event (e.g. archive transitioned to Active).</summary>
    Information,

    /// <summary>Event that may surprise an operator (e.g. extension enum value override).</summary>
    Warning,

    /// <summary>Recoverable error worth surfacing (e.g. failed background-step rolled back).</summary>
    Error,

    /// <summary>Unrecoverable failure of a system-critical operation.</summary>
    Critical,
}

/// <summary>
/// A categorised, rendered audit event published by an engine-side audit-trail forwarder and
/// consumed by a host-supplied <see cref="IAuditEventSink"/>. Carries enough information for
/// host sinks to persist (event log), forward (message bus), or simply log it, without needing
/// to know which typed forwarder produced it.
/// </summary>
/// <param name="TenantId">
/// The tenant the event belongs to, or <c>null</c> for the system tenant (e.g. import of
/// system models). Sinks use this for routing (per-tenant event log vs. system event log).
/// </param>
/// <param name="Level">Severity classification.</param>
/// <param name="Category">
/// Stable dotted identifier describing the kind of event (e.g.
/// <c>CkModelImport.ExtensibleEnumOverride</c>, <c>Archive.Transition</c>). Sinks can use this
/// for filtering or to choose a finer-grained source identifier in the event log.
/// </param>
/// <param name="Message">
/// Human-readable rendered message. Forwarders are responsible for producing this in a
/// reader-friendly form including the structured values from <see cref="Metadata"/>. The
/// platform event log stores this verbatim.
/// </param>
public sealed record AuditEvent(
    string? TenantId,
    AuditEventLevel Level,
    string Category,
    string Message)
{
    /// <summary>
    /// Optional structured payload corresponding to the placeholders in the rendered
    /// <see cref="Message"/>. Sinks that emit structured logs or push to a message bus use
    /// this; sinks that only persist a flat event-log entry can ignore it.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
