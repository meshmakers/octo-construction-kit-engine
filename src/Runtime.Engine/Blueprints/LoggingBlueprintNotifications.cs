using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Default in-process implementation of <see cref="IBlueprintNotifications"/>
/// that writes notifications to the logger. Hosts that want fan-out via the
/// distribution event hub replace this registration with an adapter.
/// </summary>
internal sealed class LoggingBlueprintNotifications : IBlueprintNotifications
{
    private readonly ILogger<LoggingBlueprintNotifications> _logger;

    public LoggingBlueprintNotifications(ILogger<LoggingBlueprintNotifications> logger)
    {
        _logger = logger;
    }

    public Task NotifyAppliedAsync(BlueprintAppliedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Blueprint applied: tenant={TenantId} blueprint={BlueprintId} mode={Mode} added={Added} updated={Updated} deleted={Deleted} correlation={CorrelationId}",
            notification.TenantId, notification.BlueprintId, notification.ApplicationMode,
            notification.EntitiesAdded, notification.EntitiesUpdated, notification.EntitiesDeleted,
            notification.CorrelationId);
        return Task.CompletedTask;
    }

    public Task NotifyUpdatedAsync(BlueprintUpdatedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Blueprint updated: tenant={TenantId} blueprint={BlueprintId} from={FromVersion} mode={UpdateMode} added={Added} updated={Updated} deleted={Deleted} backup={BackupId} correlation={CorrelationId}",
            notification.TenantId, notification.BlueprintId, notification.FromVersion, notification.UpdateMode,
            notification.EntitiesAdded, notification.EntitiesUpdated, notification.EntitiesDeleted,
            notification.BackupId, notification.CorrelationId);
        return Task.CompletedTask;
    }

    public Task NotifyRolledBackAsync(BlueprintRolledBackNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Blueprint rolled back: tenant={TenantId} blueprint={BlueprintId} backup={BackupId} correlation={CorrelationId}",
            notification.TenantId, notification.BlueprintId, notification.BackupId, notification.CorrelationId);
        return Task.CompletedTask;
    }

    public Task NotifyUninstalledAsync(BlueprintUninstalledNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Blueprint uninstalled: tenant={TenantId} blueprint={BlueprintId} cascaded={CascadedCount} correlation={CorrelationId}",
            notification.TenantId, notification.BlueprintId, notification.CascadedDependencies.Count,
            notification.CorrelationId);
        return Task.CompletedTask;
    }

    public Task NotifyOperationFailedAsync(BlueprintOperationFailedNotification notification, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Blueprint operation failed: tenant={TenantId} blueprint={BlueprintId} operation={Operation} error={Error} correlation={CorrelationId}",
            notification.TenantId, notification.BlueprintId, notification.Operation,
            notification.ErrorMessage, notification.CorrelationId);
        return Task.CompletedTask;
    }
}
