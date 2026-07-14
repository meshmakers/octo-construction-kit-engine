using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IArchiveAuditTrail"/> implementation. Translates typed archive-lifecycle
/// calls into <see cref="AuditEvent"/>s and publishes them through <see cref="IAuditEventSink"/>.
/// Replaces <see cref="LoggingArchiveAuditTrail"/> as the engine default.
/// </summary>
public sealed class ForwardingArchiveAuditTrail : IArchiveAuditTrail
{
    private readonly IAuditEventSink _sink;

    /// <summary>Constructor.</summary>
    public ForwardingArchiveAuditTrail(IAuditEventSink sink)
    {
        _sink = sink;
    }

    /// <inheritdoc />
    public Task RecordTransitionAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        CkArchiveStatus from,
        CkArchiveStatus to,
        string? reason)
    {
        var level = reason is null ? AuditEventLevel.Information : AuditEventLevel.Warning;
        var message = reason is null
            ? $"Archive {archiveRtId} transitioned {from} → {to}."
            : $"Archive {archiveRtId} transitioned {from} → {to}: {reason}";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            level,
            "Archive.Transition",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["archiveRtId"] = archiveRtId.ToString(),
                ["fromStatus"] = from.ToString(),
                ["toStatus"] = to.ToString(),
                ["reason"] = reason,
            }
        });
    }

    /// <inheritdoc />
    public Task RecordDeletionAsync(string tenantId, OctoObjectId archiveRtId, CkArchiveStatus statusAtDeletion)
    {
        var message = $"Archive {archiveRtId} deleted (was {statusAtDeletion}).";
        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Information,
            "Archive.Deletion",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["archiveRtId"] = archiveRtId.ToString(),
                ["statusAtDeletion"] = statusAtDeletion.ToString(),
            }
        });
    }

    /// <inheritdoc />
    public Task RecordRollupRunAsync(
        string tenantId,
        OctoObjectId rollupRtId,
        DateTime bucketStart,
        DateTime bucketEnd,
        int rowsWritten,
        TimeSpan elapsed)
    {
        var message =
            $"Rollup {rollupRtId} committed bucket [{bucketStart:O}, {bucketEnd:O}): " +
            $"{rowsWritten} rows in {elapsed.TotalMilliseconds:F0}ms.";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Information,
            "Archive.RollupRun",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["rollupRtId"] = rollupRtId.ToString(),
                ["bucketStart"] = bucketStart,
                ["bucketEnd"] = bucketEnd,
                ["rowsWritten"] = rowsWritten,
                ["elapsedMs"] = elapsed.TotalMilliseconds,
            }
        });
    }

    /// <inheritdoc />
    public Task RecordFreezeAsync(
        string tenantId,
        OctoObjectId rollupRtId,
        DateTime frozenUntil,
        string? reason)
    {
        var level = reason is null ? AuditEventLevel.Information : AuditEventLevel.Warning;
        var message = reason is null
            ? $"Rollup {rollupRtId} frozen until {frozenUntil:O}."
            : $"Rollup {rollupRtId} frozen until {frozenUntil:O}: {reason}";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            level,
            "Archive.RollupFreeze",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["rollupRtId"] = rollupRtId.ToString(),
                ["frozenUntil"] = frozenUntil,
                ["reason"] = reason,
            }
        });
    }

    /// <inheritdoc />
    public Task RecordRecomputeRunAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime rangeStart,
        DateTime rangeEnd,
        int rowsProcessed,
        int windowsProcessed,
        TimeSpan elapsed)
    {
        var message =
            $"Archive {archiveRtId} recomputed range [{rangeStart:O}, {rangeEnd:O}): " +
            $"{windowsProcessed} windows / {rowsProcessed} rows in {elapsed.TotalMilliseconds:F0}ms.";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Information,
            "Archive.Recompute",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["archiveRtId"] = archiveRtId.ToString(),
                ["rangeStart"] = rangeStart,
                ["rangeEnd"] = rangeEnd,
                ["rowsProcessed"] = rowsProcessed,
                ["windowsProcessed"] = windowsProcessed,
                ["elapsedMs"] = elapsed.TotalMilliseconds,
            }
        });
    }

    /// <inheritdoc />
    public Task RecordRecomputeFailureAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime rangeStart,
        DateTime rangeEnd,
        string reason)
    {
        var message =
            $"Archive {archiveRtId} recompute of range [{rangeStart:O}, {rangeEnd:O}) failed: {reason}";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Warning,
            "Archive.RecomputeFailure",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["archiveRtId"] = archiveRtId.ToString(),
                ["rangeStart"] = rangeStart,
                ["rangeEnd"] = rangeEnd,
                ["reason"] = reason,
            }
        });
    }

    /// <inheritdoc />
    public Task RecordRetroReachCappedAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime consumedWatermark,
        DateTime cappedFloor,
        TimeSpan cap)
    {
        var message =
            $"Archive {archiveRtId}: a retroactive write reached beyond the automatic recompute cap " +
            $"({cap.TotalMilliseconds:F0}ms before the consumed watermark {consumedWatermark:O}); the " +
            $"out-of-reach tail (older than {cappedFloor:O}) was not scheduled automatically. Run a " +
            $"manual recomputeArchive over the deeper range if it must be corrected.";

        return _sink.PublishAsync(new AuditEvent(
            tenantId,
            AuditEventLevel.Warning,
            "Archive.RetroReachCapped",
            message)
        {
            Metadata = new Dictionary<string, object?>
            {
                ["archiveRtId"] = archiveRtId.ToString(),
                ["consumedWatermark"] = consumedWatermark,
                ["cappedFloor"] = cappedFloor,
                ["capMs"] = cap.TotalMilliseconds,
            }
        });
    }
}
