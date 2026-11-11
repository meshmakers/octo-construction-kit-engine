namespace Meshmakers.Octo.Runtime.Engine.AuditTrails;

/// <summary>
/// Options of <see cref="AggregatingAuditEventSink"/> (AB#7279), bound from the host configuration
/// section <see cref="SectionName"/> (e.g. <c>AuditEventAggregation__Window=00:05:00</c>).
/// </summary>
public sealed class AuditEventAggregationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AuditEventAggregation";

    /// <summary>
    /// Whether repeated events with an <see cref="Meshmakers.Octo.Runtime.Contracts.AuditTrails.AuditEvent.AggregationKey"/>
    /// are aggregated. <c>false</c> publishes every event one by one, as before AB#7279.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Length of the aggregation window. The first occurrence of a key is published at once; the
    /// repetitions within the window are published as one summary when the window ends. Zero or
    /// negative disables aggregation. Default 15 minutes.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum number of keys held in memory. When a new key arrives at the cap, the key seen least
    /// recently is evicted and its pending summary is published first. Default 1000.
    /// </summary>
    public int MaxKeys { get; set; } = 1000;
}
