using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Centralised <see cref="Meter"/> and <see cref="ActivitySource"/> for the StreamData subsystem.
/// Per concept §13: metrics, structured logs (handled separately via <c>ILogger</c>) and traces
/// share a single namespace so deployments can subscribe in one place.
/// </summary>
/// <remarks>
/// The CrateDB-side counters (insert/query duration, points, connections) live in their own meter
/// (<c>Meshmakers.Octo.StreamData.Crate</c>) inside the CrateDb project. This file owns the
/// engine-side, DB-neutral signals: archive lifecycle counters, durations, activity spans.
/// </remarks>
public static class StreamDataDiagnostics
{
    /// <summary>
    /// Meter name for the engine-side StreamData metrics. Matches the activity-source name so
    /// OpenTelemetry / Prometheus exporters need a single subscription.
    /// </summary>
    public const string MeterName = "Meshmakers.Octo.StreamData";

    /// <summary>
    /// Activity source name used for archive-lifecycle and other engine-side traces.
    /// </summary>
    public const string ActivitySourceName = MeterName;

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Counter incremented on each successful archive status transition. Tags:
    /// <c>tenant</c> (when available), <c>archive</c>, <c>from</c>, <c>to</c>.
    /// </summary>
    public static readonly Counter<long> StatusTransitions =
        Meter.CreateCounter<long>(
            "streamdata.archive.status_transitions",
            unit: "{transition}",
            description: "Count of successful CkArchive status transitions, tagged by from/to/archive/tenant.");

    /// <summary>
    /// Histogram of archive activation duration in milliseconds. Recorded for both the
    /// successful and the Failed outcome (<c>outcome</c> tag).
    /// </summary>
    public static readonly Histogram<double> ActivationDurationMs =
        Meter.CreateHistogram<double>(
            "streamdata.archive.activation_duration_ms",
            unit: "ms",
            description: "Wall-clock time spent in EnsureArchiveCreatedAsync during activation, tagged by outcome.");

    /// <summary>
    /// Counter incremented when an archive deletion completes (one per archive).
    /// </summary>
    public static readonly Counter<long> Deletions =
        Meter.CreateCounter<long>(
            "streamdata.archive.deletions",
            unit: "{deletion}",
            description: "Count of completed CkArchive deletions.");

    /// <summary>
    /// Activity source for engine-side StreamData operations. Top-level span names map directly
    /// to <c>IArchiveLifecycleService</c> methods (<c>archive.activate</c>, <c>archive.disable</c>,
    /// etc.). Span attributes: <c>streamdata.archive.rtid</c>, <c>streamdata.archive.from_status</c>,
    /// <c>streamdata.archive.to_status</c>.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}
