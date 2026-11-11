using System.Diagnostics;
using System.Diagnostics.Metrics;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;

namespace Meshmakers.Octo.Runtime.Engine.AuditTrails;

/// <summary>
/// The audit events of AuditOnly data-permission violations (AB#4973, AB#7035) and their counter
/// (AB#7279). Every publisher (the read filter of the MongoDB repository, the write funnel and the
/// public permission check) builds its event here, so category, message, metadata, aggregation key
/// and metric stay identical. Each event carries an <see cref="AuditEvent.AggregationKey"/> of
/// (category, subject, CK type, action, variant): published through <see cref="AggregatingAuditEventSink"/>
/// the first occurrence is reported at once and repetitions as one summary per window, while
/// <see cref="Violations"/> counts every single occurrence.
/// </summary>
/// <remarks>
/// Hosts must register <see cref="MeterName"/> with their meter provider (octo-common-services
/// <c>ObservabilityBuilder</c>) - an unregistered meter drops every measurement silently (AB#5430). The
/// counter's labels are the aggregation key itself (tenant, subject, type, action), so its series are
/// bounded by the same set of keys the aggregation holds; no per-entity or per-query value is a label.
/// </remarks>
public static class DataPermissionAudit
{
    /// <summary>Audit category of a read violation (the read policy did not cover the caller).</summary>
    public const string ReadViolationCategory = "DataPermissions.ReadViolation";

    /// <summary>Audit category of a write violation (the write or delete policy did not cover the caller).</summary>
    public const string WriteViolationCategory = "DataPermissions.WriteViolation";

    /// <summary>Meter name.</summary>
    public const string MeterName = "Meshmakers.Octo.DataPermissions";

    /// <summary>Name of the violation counter.</summary>
    public const string ViolationsMetricName = "octo.datapermissions.audit.violations";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Incremented on every AuditOnly data-permission violation, aggregated or not. Tags: <c>tenant</c>,
    /// <c>subject</c>, <c>type</c> (CK type id) and <c>action</c> (<see cref="Actions"/>).
    /// </summary>
    public static readonly Counter<long> Violations = Meter.CreateCounter<long>(
        ViolationsMetricName,
        unit: "{violation}",
        description: "Count of AuditOnly data-permission violations (not blocked), including the aggregated ones.");

    /// <summary>The action values of the events and of the <c>action</c> tag.</summary>
    public static class Actions
    {
        /// <summary>A query read entities of the type.</summary>
        public const string Read = "read";

        /// <summary>An update addressed entities of the type through the caller's read filter.</summary>
        public const string Update = "update";

        /// <summary>A replace addressed entities of the type through the caller's read filter.</summary>
        public const string Replace = "replace";

        /// <summary>A delete addressed entities of the type (read filter or delete policy).</summary>
        public const string Delete = "delete";

        /// <summary>A create or modification was checked against the write policy.</summary>
        public const string Write = "write";

        /// <summary>Maps a data-permission action to its tag value.</summary>
        public static string From(RtDataAction action) => action switch
        {
            RtDataAction.Read => Read,
            RtDataAction.Delete => Delete,
            _ => Write,
        };
    }

    /// <summary>
    /// Publishes (and counts) a read violation: <paramref name="subjectId" /> read, or addressed with the
    /// read filter of an update, replace or delete (<paramref name="action" />), entities of
    /// <paramref name="ckTypeId" /> without a read grant.
    /// </summary>
    /// <param name="auditEventSink">Sink; the event is only counted when <c>null</c>.</param>
    /// <param name="tenantId">Tenant.</param>
    /// <param name="subjectId">Caller.</param>
    /// <param name="ckTypeId">Semantic-versioned full name of the CK type.</param>
    /// <param name="action">One of <see cref="Actions"/>: read, update, replace or delete.</param>
    public static Task PublishReadViolationAsync(IAuditEventSink? auditEventSink, string tenantId,
        string? subjectId, string ckTypeId, string action)
    {
        var subject = subjectId ?? "<unknown>";
        var message = action == Actions.Read
            ? $"Subject '{subject}' read protected type '{ckTypeId}' without a grant (AuditOnly policy — not blocked)."
            : $"Subject '{subject}' performed '{action}' on protected type '{ckTypeId}' without a read grant " +
              "(AuditOnly policy — not blocked).";
        return PublishAsync(auditEventSink, tenantId, ReadViolationCategory, subject, ckTypeId, action, null,
            message);
    }

    /// <summary>
    /// Publishes (and counts) a write violation: <paramref name="subjectId" /> performed
    /// <paramref name="action" /> on <paramref name="ckTypeId" /> without a grant, or - with
    /// <paramref name="entityNotVisible" /> - changed an entity an AuditOnly read policy hides from it (AB#7126).
    /// </summary>
    /// <param name="auditEventSink">Sink; the event is only counted when <c>null</c>.</param>
    /// <param name="tenantId">Tenant.</param>
    /// <param name="subjectId">Caller.</param>
    /// <param name="ckTypeId">Semantic-versioned full name of the CK type.</param>
    /// <param name="action">Write or delete.</param>
    /// <param name="entityNotVisible">The visibility variant of the write funnel.</param>
    public static Task PublishWriteViolationAsync(IAuditEventSink? auditEventSink, string tenantId,
        string? subjectId, string ckTypeId, RtDataAction action, bool entityNotVisible = false)
    {
        var subject = subjectId ?? "<unknown>";
        var message = entityNotVisible
            ? $"Subject '{subject}' performed '{action}' on an entity of protected type '{ckTypeId}' that it cannot " +
              "see (AuditOnly policy — not blocked)."
            : $"Subject '{subject}' performed '{action}' on protected type '{ckTypeId}' without a grant " +
              "(AuditOnly policy — not blocked).";
        return PublishAsync(auditEventSink, tenantId, WriteViolationCategory, subject, ckTypeId,
            Actions.From(action), entityNotVisible ? "not-visible" : null, message);
    }

    private static Task PublishAsync(IAuditEventSink? auditEventSink, string tenantId, string category,
        string subject, string ckTypeId, string action, string? variant, string message)
    {
        var tags = new TagList
        {
            { "tenant", tenantId },
            { "subject", subject },
            { "type", ckTypeId },
            { "action", action },
        };
        Violations.Add(1, tags);

        if (auditEventSink is null)
        {
            return Task.CompletedTask;
        }

        var auditEvent = new AuditEvent(tenantId, AuditEventLevel.Warning, category, message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["SubjectId"] = subject,
                ["CkTypeId"] = ckTypeId,
                ["Action"] = action,
            },
            AggregationKey = variant is null
                ? $"{category}|{subject}|{ckTypeId}|{action}"
                : $"{category}|{subject}|{ckTypeId}|{action}|{variant}",
        };
        return auditEventSink.PublishAsync(auditEvent);
    }
}
