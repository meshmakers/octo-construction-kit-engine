using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IArchiveAuditTrail"/> implementation that just logs transitions and
/// deletions. Stand-in until a host project bridges <see cref="IArchiveAuditTrail"/> to the
/// platform notification/event repository (concept §14). Safe to keep as the default forever:
/// even with a real bus in place, structured logs of these transitions are useful.
/// </summary>
public sealed class LoggingArchiveAuditTrail : IArchiveAuditTrail
{
    private readonly ILogger<LoggingArchiveAuditTrail> _logger;

    /// <summary>Constructor.</summary>
    public LoggingArchiveAuditTrail(ILogger<LoggingArchiveAuditTrail> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RecordTransitionAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        CkArchiveStatus from,
        CkArchiveStatus to,
        string? reason)
    {
        if (reason is null)
        {
            _logger.LogInformation(
                "Archive {ArchiveRtId} (tenant {TenantId}) transitioned {FromStatus} → {ToStatus}",
                archiveRtId, tenantId, from, to);
        }
        else
        {
            _logger.LogWarning(
                "Archive {ArchiveRtId} (tenant {TenantId}) transitioned {FromStatus} → {ToStatus}: {Reason}",
                archiveRtId, tenantId, from, to, reason);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordDeletionAsync(string tenantId, OctoObjectId archiveRtId, CkArchiveStatus statusAtDeletion)
    {
        _logger.LogInformation(
            "Archive {ArchiveRtId} (tenant {TenantId}) deleted (was {StatusAtDeletion})",
            archiveRtId, tenantId, statusAtDeletion);
        return Task.CompletedTask;
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
        _logger.LogInformation(
            "Rollup {RollupRtId} (tenant {TenantId}) committed bucket [{BucketStart:O}, {BucketEnd:O}): {RowsWritten} rows in {ElapsedMs}ms",
            rollupRtId, tenantId, bucketStart, bucketEnd, rowsWritten, elapsed.TotalMilliseconds);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordFreezeAsync(
        string tenantId,
        OctoObjectId rollupRtId,
        DateTime frozenUntil,
        string? reason)
    {
        if (reason is null)
        {
            _logger.LogInformation(
                "Rollup {RollupRtId} (tenant {TenantId}) frozen until {FrozenUntil:O}",
                rollupRtId, tenantId, frozenUntil);
        }
        else
        {
            _logger.LogWarning(
                "Rollup {RollupRtId} (tenant {TenantId}) frozen until {FrozenUntil:O}: {Reason}",
                rollupRtId, tenantId, frozenUntil, reason);
        }
        return Task.CompletedTask;
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
        _logger.LogInformation(
            "Archive {ArchiveRtId} (tenant {TenantId}) recomputed range [{RangeStart:O}, {RangeEnd:O}): {WindowsProcessed} windows / {RowsProcessed} rows in {ElapsedMs}ms",
            archiveRtId, tenantId, rangeStart, rangeEnd, windowsProcessed, rowsProcessed, elapsed.TotalMilliseconds);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordRecomputeFailureAsync(
        string tenantId,
        OctoObjectId archiveRtId,
        DateTime rangeStart,
        DateTime rangeEnd,
        string reason)
    {
        _logger.LogWarning(
            "Archive {ArchiveRtId} (tenant {TenantId}) recompute of range [{RangeStart:O}, {RangeEnd:O}) failed: {Reason}",
            archiveRtId, tenantId, rangeStart, rangeEnd, reason);
        return Task.CompletedTask;
    }
}
