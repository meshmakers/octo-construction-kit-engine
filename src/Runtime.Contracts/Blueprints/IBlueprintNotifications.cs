using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Audit-trail sink for blueprint lifecycle events.
/// </summary>
/// <remarks>
/// The engine raises these notifications at the boundaries of every blueprint
/// operation. The default in-process implementation logs only. Service-layer
/// hosts (e.g. octo-asset-repo-services) replace it with an adapter that
/// republishes the notifications to the distribution event hub.
/// </remarks>
public interface IBlueprintNotifications
{
    /// <summary>Raised after a blueprint was successfully applied to a tenant.</summary>
    Task NotifyAppliedAsync(
        BlueprintAppliedNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>Raised after a tenant's blueprint was updated to a new version.</summary>
    Task NotifyUpdatedAsync(
        BlueprintUpdatedNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>Raised after a blueprint was uninstalled from a tenant.</summary>
    Task NotifyUninstalledAsync(
        BlueprintUninstalledNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>Raised when any blueprint operation fails.</summary>
    Task NotifyOperationFailedAsync(
        BlueprintOperationFailedNotification notification,
        CancellationToken cancellationToken = default);
}

/// <param name="TenantId">Target tenant.</param>
/// <param name="BlueprintId">Blueprint that was applied.</param>
/// <param name="ApplicationMode">How the application occurred (Initial, Update, Migration, ReApply).</param>
/// <param name="EntitiesAdded">Entities created during application.</param>
/// <param name="EntitiesUpdated">Entities updated during application.</param>
/// <param name="EntitiesDeleted">Entities deleted during application.</param>
/// <param name="CorrelationId">Correlates this notification with other events from the same operation.</param>
/// <param name="Timestamp">When the operation completed.</param>
public record BlueprintAppliedNotification(
    string TenantId,
    BlueprintId BlueprintId,
    BlueprintApplicationMode ApplicationMode,
    int EntitiesAdded,
    int EntitiesUpdated,
    int EntitiesDeleted,
    Guid CorrelationId,
    DateTime Timestamp);

/// <param name="TenantId">Target tenant.</param>
/// <param name="BlueprintId">New blueprint version.</param>
/// <param name="FromVersion">Previous blueprint version, or null if the tenant had no prior version.</param>
/// <param name="UpdateMode">Update strategy used.</param>
/// <param name="EntitiesAdded">Entities created during update.</param>
/// <param name="EntitiesUpdated">Entities updated during update.</param>
/// <param name="EntitiesDeleted">Entities deleted during update.</param>
/// <param name="CorrelationId">Correlates this notification with other events from the same operation.</param>
/// <param name="Timestamp">When the operation completed.</param>
public record BlueprintUpdatedNotification(
    string TenantId,
    BlueprintId BlueprintId,
    BlueprintId? FromVersion,
    BlueprintUpdateMode UpdateMode,
    int EntitiesAdded,
    int EntitiesUpdated,
    int EntitiesDeleted,
    Guid CorrelationId,
    DateTime Timestamp);

/// <param name="TenantId">Target tenant.</param>
/// <param name="BlueprintId">Blueprint that was uninstalled.</param>
/// <param name="CascadedDependencies">Dependencies that were uninstalled together.</param>
/// <param name="CorrelationId">Correlates this notification with other events from the same operation.</param>
/// <param name="Timestamp">When the operation completed.</param>
public record BlueprintUninstalledNotification(
    string TenantId,
    BlueprintId BlueprintId,
    IReadOnlyList<BlueprintId> CascadedDependencies,
    Guid CorrelationId,
    DateTime Timestamp);

/// <param name="TenantId">Target tenant.</param>
/// <param name="BlueprintId">Blueprint involved, or null if the failure happened before identification.</param>
/// <param name="Operation">Operation name, e.g. "Apply", "Update", "Uninstall".</param>
/// <param name="ErrorMessage">Human-readable error description.</param>
/// <param name="CorrelationId">Correlates this notification with other events from the same operation.</param>
/// <param name="Timestamp">When the failure occurred.</param>
public record BlueprintOperationFailedNotification(
    string TenantId,
    BlueprintId? BlueprintId,
    string Operation,
    string ErrorMessage,
    Guid CorrelationId,
    DateTime Timestamp);
