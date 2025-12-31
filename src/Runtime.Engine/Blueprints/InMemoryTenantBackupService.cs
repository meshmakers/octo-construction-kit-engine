using System.Collections.Concurrent;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// In-memory implementation of tenant backup service.
/// This is suitable for testing and scenarios where persistence is handled externally.
/// For production use, implement a persistent storage backend.
/// </summary>
internal class InMemoryTenantBackupService : ITenantBackupService
{
    private readonly ConcurrentDictionary<string, List<BackupInfo>> _backups = new();
    private readonly ILogger<InMemoryTenantBackupService> _logger;

    public InMemoryTenantBackupService(ILogger<InMemoryTenantBackupService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<BackupInfo> CreateBackupAsync(
        string tenantId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var backupId = $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        var backupInfo = new BackupInfo
        {
            BackupId = backupId,
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow,
            Reason = reason,
            BackupType = BackupType.BlueprintUpdate,
            EntityCount = 0 // Would be populated with actual entity count
        };

        _backups.AddOrUpdate(
            tenantId,
            _ => new List<BackupInfo> { backupInfo },
            (_, existing) =>
            {
                existing.Add(backupInfo);
                return existing;
            });

        _logger.LogInformation("Created backup {BackupId} for tenant {TenantId}: {Reason}",
            backupId, tenantId, reason);

        return Task.FromResult(backupInfo);
    }

    /// <inheritdoc />
    public Task<BackupRestoreResult> RestoreBackupAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Restoring backup {BackupId} for tenant {TenantId}", backupId, tenantId);

        var result = new BackupRestoreResult();

        if (!_backups.TryGetValue(tenantId, out var backups))
        {
            result.Success = false;
            result.Errors.Add($"No backups found for tenant {tenantId}");
            return Task.FromResult(result);
        }

        var backup = backups.FirstOrDefault(b => b.BackupId == backupId);
        if (backup == null)
        {
            result.Success = false;
            result.Errors.Add($"Backup {backupId} not found");
            return Task.FromResult(result);
        }

        // In a real implementation, this would:
        // 1. Load the backup data
        // 2. Clear or mark existing entities
        // 3. Restore entities from backup
        // 4. Update tenant state

        result.Success = true;
        result.RestoredBackup = backup;
        result.EntitiesRestored = backup.EntityCount;
        result.Warnings.Add("In-memory backup service - actual data restore not implemented");

        _logger.LogInformation("Restored backup {BackupId} for tenant {TenantId}: {EntityCount} entities",
            backupId, tenantId, result.EntitiesRestored);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_backups.TryGetValue(tenantId, out var backups))
        {
            return Task.FromResult<IReadOnlyList<BackupInfo>>(
                backups.OrderByDescending(b => b.CreatedAt).ToList());
        }

        return Task.FromResult<IReadOnlyList<BackupInfo>>(Array.Empty<BackupInfo>());
    }

    /// <inheritdoc />
    public Task<BackupInfo?> GetBackupAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default)
    {
        if (_backups.TryGetValue(tenantId, out var backups))
        {
            var backup = backups.FirstOrDefault(b => b.BackupId == backupId);
            return Task.FromResult<BackupInfo?>(backup);
        }

        return Task.FromResult<BackupInfo?>(null);
    }

    /// <inheritdoc />
    public Task<bool> DeleteBackupAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default)
    {
        if (_backups.TryGetValue(tenantId, out var backups))
        {
            var removed = backups.RemoveAll(b => b.BackupId == backupId);
            if (removed > 0)
            {
                _logger.LogInformation("Deleted backup {BackupId} for tenant {TenantId}", backupId, tenantId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }
}
