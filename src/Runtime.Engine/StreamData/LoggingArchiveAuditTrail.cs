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
}
