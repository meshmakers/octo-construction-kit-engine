using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Implements blueprint application to tenants.
/// </summary>
internal class BlueprintService : IBlueprintService
{
    private readonly ICkCacheService _ckCacheService;
    private readonly IBlueprintCatalogManager _blueprintCatalogManager;
    private readonly ITenantBlueprintHistory _blueprintHistory;
    private readonly ITenantBackupService _backupService;
    private readonly IBlueprintMigrationExecutor _migrationExecutor;
    private readonly IBlueprintMigrationParser _migrationParser;
    private readonly ICkModelUpgradeService _ckModelUpgradeService;
    private readonly IRuntimeRepositoryProvider _runtimeRepositoryProvider;
    private readonly IImportRtModelCommand _importRtModelCommand;
    private readonly IRtYamlSerializer _rtYamlSerializer;
    private readonly IBlueprintNotifications _notifications;
    private readonly IBlueprintDependencyResolver _dependencyResolver;
    private readonly ITenantBlueprintInstallations _installations;
    private readonly ILogger<BlueprintService> _logger;

    // System CK attribute identifiers for blueprint tracking.
    // RtCkId is "{ModelId}/{ElementId}" — the attribute lives at the model
    // level (compiled CkAttributeId = "System-2.x.x/RtBlueprintSource-1"),
    // so the element id is just the bare attribute name. The fact that the
    // attribute is *declared* on the Entity type does not appear in its id.
    private static readonly RtCkId<CkAttributeId> RtBlueprintSourceAttrId =
        new("System/RtBlueprintSource");

    private static readonly RtCkId<CkAttributeId> RtBlueprintLockedAttrId =
        new("System/RtBlueprintLocked");

    private static readonly RtCkId<CkAttributeId> RtBlueprintAppliedAtAttrId =
        new("System/RtBlueprintAppliedAt");

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintService"/>
    /// </summary>
    public BlueprintService(
        ICkCacheService ckCacheService,
        IBlueprintCatalogManager blueprintCatalogManager,
        ITenantBlueprintHistory blueprintHistory,
        ITenantBackupService backupService,
        IBlueprintMigrationExecutor migrationExecutor,
        IBlueprintMigrationParser migrationParser,
        ICkModelUpgradeService ckModelUpgradeService,
        IRuntimeRepositoryProvider runtimeRepositoryProvider,
        IImportRtModelCommand importRtModelCommand,
        IRtYamlSerializer rtYamlSerializer,
        IBlueprintNotifications notifications,
        IBlueprintDependencyResolver dependencyResolver,
        ITenantBlueprintInstallations installations,
        ILogger<BlueprintService> logger)
    {
        _ckCacheService = ckCacheService;
        _blueprintCatalogManager = blueprintCatalogManager;
        _blueprintHistory = blueprintHistory;
        _backupService = backupService;
        _migrationExecutor = migrationExecutor;
        _migrationParser = migrationParser;
        _ckModelUpgradeService = ckModelUpgradeService;
        _runtimeRepositoryProvider = runtimeRepositoryProvider;
        _importRtModelCommand = importRtModelCommand;
        _rtYamlSerializer = rtYamlSerializer;
        _notifications = notifications;
        _dependencyResolver = dependencyResolver;
        _installations = installations;
        _logger = logger;
    }

    /// <summary>
    /// Stamps blueprint provenance attributes on every entity in the seed root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>rtBlueprintSource</c> and <c>rtBlueprintAppliedAt</c> are always set to the
    /// blueprint id and the apply timestamp respectively — they describe the apply
    /// operation, not the entity's data, so the seed cannot override them.
    /// </para>
    /// <para>
    /// <c>rtBlueprintLocked</c> defaults to <c>true</c> (the entity is managed by the
    /// blueprint), but a seed can explicitly set it to <c>false</c> to mark an entity
    /// as user-modifiable. Those values are preserved here.
    /// </para>
    /// </remarks>
    private static void StampBlueprintTags(
        RtModelRootTcDto root,
        BlueprintId blueprintId,
        DateTime appliedAt)
    {
        foreach (var entity in root.Entities)
        {
            SetOrReplaceAttribute(entity, RtBlueprintSourceAttrId, blueprintId.FullName);
            SetOrReplaceAttribute(entity, RtBlueprintAppliedAtAttrId, appliedAt);
            SetAttributeIfMissing(entity, RtBlueprintLockedAttrId, true);
        }
    }

    private static void SetOrReplaceAttribute(
        RtEntityTcDto entity,
        RtCkId<CkAttributeId> attributeId,
        object value)
    {
        var existing = entity.Attributes.FirstOrDefault(a => a.Id.Equals(attributeId));
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            entity.Attributes.Add(new RtAttributeTcDto { Id = attributeId, Value = value });
        }
    }

    private static void SetAttributeIfMissing(
        RtEntityTcDto entity,
        RtCkId<CkAttributeId> attributeId,
        object value)
    {
        if (entity.Attributes.All(a => !a.Id.Equals(attributeId)))
        {
            entity.Attributes.Add(new RtAttributeTcDto { Id = attributeId, Value = value });
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintApplicationResult> ApplyBlueprintAsync(
        string tenantId,
        BlueprintId blueprintId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var operationResult = new OperationResult();
        var loadedCkModels = new List<CkModelIdVersionRange>();
        var appliedSeedDataFiles = new List<string>();
        var entitiesCreated = 0;
        var correlationId = Guid.NewGuid();
        var applicationMode = force ? BlueprintApplicationMode.ReApply : BlueprintApplicationMode.Initial;

        try
        {
            _logger.LogInformation("Applying blueprint {BlueprintId} to tenant {TenantId}",
                blueprintId, tenantId);

            // 1. Resolve the full dependency closure (topo-sorted; deps first, root last).
            var resolution = await _dependencyResolver
                .ResolveAsync(blueprintId, cancellationToken).ConfigureAwait(false);

            if (!resolution.Success)
            {
                foreach (var conflict in resolution.Conflicts)
                {
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Error,
                        null,
                        50,
                        $"{conflict.ConflictType}: {conflict.Description}"));
                }
                await NotifyApplyFailedAsync(tenantId, blueprintId, operationResult, correlationId).ConfigureAwait(false);
                return BlueprintApplicationResult.Failed(operationResult);
            }

            foreach (var warning in resolution.Warnings)
            {
                operationResult.AddMessage(new OperationMessage(
                    MessageLevel.Warning, null, 51, warning));
            }

            // 2. Tenant cache bootstrap.
            if (!_ckCacheService.IsTenantLoaded(tenantId))
            {
                _ckCacheService.CreateTenant(tenantId);
                _logger.LogDebug("Created tenant {TenantId}", tenantId);
            }

            // 3. Aggregate all CK model dependencies across the install order and upgrade once.
            var aggregatedCkDeps = resolution.InstallOrder
                .SelectMany(bp => bp.CkModelDependencies ?? [])
                .GroupBy(d => d.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
            loadedCkModels.AddRange(aggregatedCkDeps);

            if (aggregatedCkDeps.Count > 0)
            {
                var upgradeResult = await _ckModelUpgradeService.UpgradeModelsAsync(
                        tenantId,
                        aggregatedCkDeps,
                        new CkMigrationOptions { CreateBackup = true, DryRun = false, ContinueOnError = false },
                        null,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!upgradeResult.Success)
                {
                    foreach (var error in upgradeResult.Errors)
                    {
                        operationResult.AddMessage(new OperationMessage(
                            MessageLevel.Error, null, 25, $"CK model migration failed: {error}"));
                    }
                    await NotifyApplyFailedAsync(tenantId, blueprintId, operationResult, correlationId).ConfigureAwait(false);
                    return BlueprintApplicationResult.Failed(operationResult);
                }

                foreach (var warning in upgradeResult.Warnings)
                {
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Warning, null, 26, $"CK model migration: {warning}"));
                }
            }

            // 4. Walk the topo-sorted install order. Each blueprint becomes its own
            //    installation row; dependencies are tagged IsDependency = true so
            //    uninstall can refcount.
            var rootDependencyIds = new List<BlueprintId>();

            foreach (var blueprint in resolution.InstallOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var isRoot = blueprint.BlueprintId.Equals(blueprintId);

                var existing = await _installations
                    .GetByBlueprintNameAsync(tenantId, blueprint.BlueprintId.Name, cancellationToken)
                    .ConfigureAwait(false);

                // Idempotent skip: same version already on the tenant, no --force,
                // and it's a transitive dep (not the explicitly-requested root).
                if (existing != null
                    && existing.BlueprintId.Equals(blueprint.BlueprintId)
                    && !(isRoot && force))
                {
                    if (!isRoot)
                    {
                        rootDependencyIds.Add(blueprint.BlueprintId);
                        _logger.LogDebug(
                            "Dependency {BlueprintId} already installed on tenant {TenantId}, skipping",
                            blueprint.BlueprintId, tenantId);
                        continue;
                    }
                    // Root with no force and already installed: still re-stamp to refresh
                    // LastUpdatedAt / dependency list, but skip seed-data import.
                }

                // 4a. Seed-data import (skipped on idempotent root that's just being
                //     re-recorded with refreshed metadata).
                var willImportSeed = existing == null
                    || !existing.BlueprintId.Equals(blueprint.BlueprintId)
                    || (isRoot && force);

                if (willImportSeed)
                {
                    var perBlueprintEntities = await ApplySeedDataForBlueprintAsync(
                        tenantId, blueprint, operationResult, appliedSeedDataFiles, cancellationToken)
                        .ConfigureAwait(false);

                    if (operationResult.HasErrors)
                    {
                        await NotifyApplyFailedAsync(tenantId, blueprintId, operationResult, correlationId).ConfigureAwait(false);
                        return BlueprintApplicationResult.Failed(operationResult);
                    }

                    entitiesCreated += perBlueprintEntities;
                }

                // 4b. Record the installation row.
                var installation = new BlueprintInstallation
                {
                    BlueprintId = blueprint.BlueprintId,
                    InstalledAt = existing?.InstalledAt ?? DateTime.UtcNow,
                    LastUpdatedAt = DateTime.UtcNow,
                    IsDependency = !isRoot,
                    ResolvedDependencies = isRoot
                        ? rootDependencyIds.ToList()
                        : []
                };
                await _installations.UpsertAsync(tenantId, installation, cancellationToken)
                    .ConfigureAwait(false);

                if (!isRoot)
                {
                    rootDependencyIds.Add(blueprint.BlueprintId);
                }
            }

            // 5. Append a single history entry for the root operation.
            var blueprintInfo = new TenantBlueprintInfo
            {
                BlueprintId = blueprintId,
                AppliedAt = DateTime.UtcNow,
                ApplicationMode = applicationMode,
                EntitiesCreated = entitiesCreated,
                EntitiesUpdated = 0,
                EntitiesDeleted = 0
            };

            await _blueprintHistory.AddEntryAsync(tenantId, blueprintInfo, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Blueprint {BlueprintId} applied to tenant {TenantId}: {InstallCount} blueprints installed, {SeedDataCount} seed files imported",
                blueprintId, tenantId, resolution.InstallOrder.Count, appliedSeedDataFiles.Count);

            await _notifications.NotifyAppliedAsync(
                new BlueprintAppliedNotification(
                    tenantId,
                    blueprintId,
                    applicationMode,
                    EntitiesAdded: entitiesCreated,
                    EntitiesUpdated: 0,
                    EntitiesDeleted: 0,
                    correlationId,
                    DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);

            return BlueprintApplicationResult.Success(
                tenantId,
                blueprintId,
                loadedCkModels,
                appliedSeedDataFiles,
                entitiesCreated,
                operationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying blueprint {BlueprintId} to tenant {TenantId}",
                blueprintId, tenantId);

            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                null,
                30,
                $"Failed to apply blueprint: {ex.Message}"));

            await NotifyApplyFailedAsync(tenantId, blueprintId, operationResult, correlationId).ConfigureAwait(false);
            return BlueprintApplicationResult.Failed(operationResult);
        }
    }

    private async Task NotifyApplyFailedAsync(
        string tenantId,
        BlueprintId blueprintId,
        OperationResult operationResult,
        Guid correlationId)
    {
        var errorMessage = string.Join("; ", operationResult.Messages
            .Where(m => m.MessageLevel == MessageLevel.Error)
            .Select(m => m.MessageText));

        try
        {
            await _notifications.NotifyOperationFailedAsync(
                new BlueprintOperationFailedNotification(
                    tenantId,
                    blueprintId,
                    Operation: "Apply",
                    ErrorMessage: errorMessage,
                    correlationId,
                    DateTime.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(notifyEx, "Failed to publish BlueprintOperationFailed notification");
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintValidationResult> ValidateBlueprintAsync(
        BlueprintId blueprintId,
        CancellationToken cancellationToken = default)
    {
        var operationResult = new OperationResult();
        var missingCkModels = new List<CkModelIdVersionRange>();
        var missingBlueprints = new List<BlueprintIdVersionRange>();
        var missingSeedDataFiles = new List<string>();

        try
        {
            _logger.LogDebug("Validating blueprint {BlueprintId}", blueprintId);

            // Check if blueprint exists
            var exists = await _blueprintCatalogManager.IsExistingAsync(blueprintId).ConfigureAwait(false);
            if (!exists)
            {
                operationResult.AddMessage(new OperationMessage(
                    MessageLevel.Error,
                    null,
                    40,
                    $"Blueprint '{blueprintId}' not found"));
                return BlueprintValidationResult.Invalid(
                    blueprintId, missingCkModels, missingBlueprints, missingSeedDataFiles, operationResult);
            }

            // Fetch blueprint to inspect its dependencies and seed data
            var blueprint = await _blueprintCatalogManager.GetAsync(blueprintId, operationResult)
                .ConfigureAwait(false);

            // Validate CK model dependencies exist
            // Note: This would check against the CK catalog
            // For now, we just collect the dependencies for validation

            // Validate seed data file exists
            if (!string.IsNullOrEmpty(blueprint.SeedDataPath))
            {
                var blueprintPath = await _blueprintCatalogManager
                    .GetBlueprintPathAsync(blueprintId)
                    .ConfigureAwait(false);

                var seedDataPath = Path.Combine(blueprintPath, blueprint.SeedDataPath);
                if (!File.Exists(seedDataPath))
                {
                    missingSeedDataFiles.Add(seedDataPath);
                }
            }

            if (missingCkModels.Count > 0 || missingBlueprints.Count > 0 || missingSeedDataFiles.Count > 0 || operationResult.HasErrors)
            {
                return BlueprintValidationResult.Invalid(
                    blueprintId, missingCkModels, missingBlueprints, missingSeedDataFiles, operationResult);
            }

            _logger.LogInformation("Blueprint {BlueprintId} is valid", blueprintId);
            return BlueprintValidationResult.Valid(blueprintId, operationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating blueprint {BlueprintId}", blueprintId);

            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                null,
                41,
                $"Failed to validate blueprint: {ex.Message}"));

            return BlueprintValidationResult.Invalid(
                blueprintId, missingCkModels, missingBlueprints, missingSeedDataFiles, operationResult);
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintListResult> ListBlueprintsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _blueprintCatalogManager.ListAsync(skip, take).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BlueprintSearchResult> SearchBlueprintsAsync(
        string searchTerm,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await _blueprintCatalogManager.SearchAsync(searchTerm, skip, take).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BlueprintUpdateInfo?> GetUpdateInfoAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting update info for tenant {TenantId}", tenantId);

        // 1. Get current blueprint for tenant
        var currentInfo = await _blueprintHistory.GetCurrentAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (currentInfo == null)
        {
            _logger.LogDebug("No blueprint applied to tenant {TenantId}", tenantId);
            return null;
        }

        // 2. Find available versions in catalog
        var blueprintName = currentInfo.BlueprintId.Name;
        var listResult = await _blueprintCatalogManager.ListAsync(0, 1000).ConfigureAwait(false);

        var availableVersions = listResult.Items
            .Where(item => item.BlueprintId.Name == blueprintName &&
                           item.BlueprintId.Version.CompareTo(currentInfo.BlueprintId.Version) > 0)
            .Select(item => item.BlueprintId)
            .OrderBy(id => id.Version)
            .ToList();

        if (availableVersions.Count == 0)
        {
            _logger.LogDebug("No updates available for blueprint {BlueprintId}", currentInfo.BlueprintId);
            return new BlueprintUpdateInfo
            {
                CurrentVersion = currentInfo.BlueprintId,
                AvailableVersions = [],
                RecommendedVersion = null,
                HasMigrationPath = false
            };
        }

        // 3. Check for migration scripts
        var recommendedVersion = availableVersions.LastOrDefault();
        var availableMigrations = new List<string>();
        var hasMigrationPath = false;

        if (recommendedVersion != null)
        {
            try
            {
                var operationResult = new OperationResult();
                var targetBlueprint = await _blueprintCatalogManager
                    .GetAsync(recommendedVersion, operationResult)
                    .ConfigureAwait(false);

                var migrations = targetBlueprint.Migrations ?? [];

                // Check if there's a migration from current version
                hasMigrationPath = migrations.Any(
                    m => m.FromVersion == currentInfo.BlueprintId.Version.ToString());

                availableMigrations = migrations
                    .Select(m => m.FromVersion)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check migration paths for {BlueprintId}", recommendedVersion);
            }
        }

        return new BlueprintUpdateInfo
        {
            CurrentVersion = currentInfo.BlueprintId,
            AvailableVersions = availableVersions,
            RecommendedVersion = recommendedVersion,
            HasMigrationPath = hasMigrationPath,
            AvailableMigrations = availableMigrations
        };
    }

    /// <inheritdoc />
    public async Task<BlueprintUpdatePreview> PreviewUpdateAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintUpdateMode updateMode = BlueprintUpdateMode.Merge,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Previewing update for tenant {TenantId} to version {TargetVersion} with mode {UpdateMode}",
            tenantId, targetVersion, updateMode);

        var preview = new BlueprintUpdatePreview();

        // 1. Get current blueprint for tenant
        var currentInfo = await _blueprintHistory.GetCurrentAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (currentInfo == null)
        {
            preview.Warnings.Add("No blueprint currently applied to tenant - this will be a fresh application");
            preview.EntitiesToAdd = 1; // Placeholder - actual count would come from seed data analysis
            return preview;
        }

        // 2. Validate target version exists
        var targetExists = await _blueprintCatalogManager.IsExistingAsync(targetVersion)
            .ConfigureAwait(false);

        if (!targetExists)
        {
            preview.Conflicts.Add(new BlueprintUpdateConflict
            {
                EntityId = "",
                Description = $"Target blueprint version {targetVersion} not found",
                ConflictType = ConflictType.MissingDependency,
                SuggestedResolution = ConflictResolution.Skip
            });
            return preview;
        }

        // 3. Fetch target blueprint
        var operationResult = new OperationResult();
        var targetBlueprint = await _blueprintCatalogManager
            .GetAsync(targetVersion, operationResult)
            .ConfigureAwait(false);

        if (operationResult.HasErrors)
        {
            foreach (var error in operationResult.Messages.Where(m => m.MessageLevel == MessageLevel.Error))
            {
                preview.Conflicts.Add(new BlueprintUpdateConflict
                {
                    EntityId = "",
                    Description = error.MessageText,
                    ConflictType = ConflictType.MissingDependency,
                    SuggestedResolution = ConflictResolution.Skip
                });
            }
            return preview;
        }

        // 4. Check for migration path if using Migration mode
        if (updateMode == BlueprintUpdateMode.Migration)
        {
            var migrationRef = (targetBlueprint.Migrations ?? [])
                .FirstOrDefault(m => m.FromVersion == currentInfo.BlueprintId.Version.ToString());

            if (migrationRef == null)
            {
                preview.Warnings.Add($"No migration script found from version {currentInfo.BlueprintId.Version}");
                preview.Warnings.Add("Falling back to Merge mode for preview");
            }
            else
            {
                // Load and validate migration
                try
                {
                    var blueprintPath = await _blueprintCatalogManager
                        .GetBlueprintPathAsync(targetVersion)
                        .ConfigureAwait(false);

                    var migrationPath = Path.Combine(blueprintPath, migrationRef.ScriptPath);
                    var migration = await _migrationParser.ParseAsync(migrationPath, cancellationToken)
                        .ConfigureAwait(false);

                    var validationResult = await _migrationExecutor.ValidateAsync(
                        tenantId, migration, cancellationToken)
                        .ConfigureAwait(false);

                    if (!validationResult.IsValid)
                    {
                        foreach (var error in validationResult.Errors)
                        {
                            preview.Conflicts.Add(new BlueprintUpdateConflict
                            {
                                EntityId = error.StepId ?? "",
                                Description = error.Message,
                                ConflictType = ConflictType.MissingDependency,
                                SuggestedResolution = ConflictResolution.Skip
                            });
                        }
                    }

                    foreach (var warning in validationResult.Warnings)
                    {
                        preview.Warnings.Add(warning.Message);
                    }

                    // Count operations from migration
                    preview.EntitiesToAdd = migration.Steps.Count(s => s.Action == MigrationActionType.Add);
                    preview.EntitiesToUpdate = migration.Steps.Count(s => s.Action == MigrationActionType.Update || s.Action == MigrationActionType.Transform);
                    preview.EntitiesToDelete = migration.Steps.Count(s => s.Action == MigrationActionType.Delete);
                }
                catch (Exception ex)
                {
                    preview.Warnings.Add($"Could not analyze migration script: {ex.Message}");
                }
            }
        }
        else
        {
            // Real diff: compare seed entities to tenant entities tagged with this
            // blueprint's source. Modes affect interpretation, not the diff itself.
            var diff = await ComputeUpdateDiffAsync(
                tenantId, targetVersion, targetBlueprint, updateMode, cancellationToken)
                .ConfigureAwait(false);

            preview.EntitiesToAdd = diff.ToAdd;
            preview.EntitiesToUpdate = diff.ToUpdate;
            preview.EntitiesToDelete = diff.ToDelete;
            preview.Warnings.AddRange(diff.Warnings);
            foreach (var conflict in diff.Conflicts)
            {
                preview.Conflicts.Add(conflict);
            }

            if (updateMode == BlueprintUpdateMode.Safe)
            {
                preview.Warnings.Add(
                    "Safe mode: only new entities will be added; existing entities are skipped");
            }
            else if (updateMode == BlueprintUpdateMode.Merge)
            {
                preview.Warnings.Add(
                    "Merge mode: locked entities (rtBlueprintLocked=true) will be updated; unlocked entities are skipped");
            }
            else if (updateMode == BlueprintUpdateMode.Full)
            {
                preview.Warnings.Add(
                    "Full mode: locked entities updated; locked entities no longer in seed will be deleted");
            }
        }

        return preview;
    }

    /// <summary>
    /// Compares a target blueprint's seed data to entities currently tagged with
    /// this blueprint's source on the tenant. Used by both PreviewUpdateAsync and
    /// (eventually) the mode-specific apply paths in Phase 2c.
    /// </summary>
    private async Task<BlueprintUpdateDiff> ComputeUpdateDiffAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintMetaRootDto targetBlueprint,
        BlueprintUpdateMode updateMode,
        CancellationToken cancellationToken)
    {
        var diff = new BlueprintUpdateDiff();

        if (string.IsNullOrEmpty(targetBlueprint.SeedDataPath))
        {
            diff.Warnings.Add("Target blueprint has no seed data; nothing to compare");
            return diff;
        }

        var blueprintPath = await _blueprintCatalogManager
            .GetBlueprintPathAsync(targetVersion).ConfigureAwait(false);
        var seedPath = Path.Combine(blueprintPath, targetBlueprint.SeedDataPath);

        if (!File.Exists(seedPath))
        {
            diff.Warnings.Add($"Seed data file not found: {seedPath}");
            return diff;
        }

        var opResult = new OperationResult();
        var seedRoot = await LoadAndTagSeedAsync(seedPath, targetVersion, opResult)
            .ConfigureAwait(false);
        if (seedRoot == null)
        {
            diff.Warnings.AddRange(opResult.Messages
                .Where(m => m.MessageLevel == MessageLevel.Error)
                .Select(m => m.MessageText));
            return diff;
        }

        var repository = await _runtimeRepositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (repository == null)
        {
            diff.Warnings.Add("Tenant repository not available; returning seed-only counts");
            diff.ToAdd = seedRoot.Entities.Count;
            return diff;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);

        foreach (var typeGroup in seedRoot.Entities.GroupBy(e => e.CkTypeId))
        {
            var ckTypeId = typeGroup.Key;
            var seedEntitiesOfType = typeGroup.ToList();

            IResultSet<RtEntity> tenantEntities;
            try
            {
                tenantEntities = await repository.GetRtEntitiesByTypeAsync(
                    session, ckTypeId, RtEntityQueryOptions.Create()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                diff.Warnings.Add(
                    $"Could not query tenant entities for type {ckTypeId}: {ex.Message}");
                diff.ToAdd += seedEntitiesOfType.Count;
                continue;
            }

            var tenantOfBlueprintByKey = new Dictionary<string, RtEntity>(StringComparer.Ordinal);
            foreach (var t in tenantEntities.Items)
            {
                var source = t.GetAttributeStringValueOrDefault("RtBlueprintSource");
                if (source == null || !IsSameBlueprintName(source, targetVersion))
                {
                    continue;
                }

                var key = t.RtWellKnownName ?? t.RtId.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    tenantOfBlueprintByKey[key] = t;
                }
            }

            var seedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var seed in seedEntitiesOfType)
            {
                var key = seed.RtWellKnownName
                    ?? (seed.RtId.Equals(OctoObjectId.Empty) ? null : seed.RtId.ToString());
                if (string.IsNullOrEmpty(key))
                {
                    // Cannot match without an identity key — treat as new.
                    diff.ToAdd++;
                    diff.EntitiesToAdd.Add(seed);
                    continue;
                }
                seedKeys.Add(key!);

                if (tenantOfBlueprintByKey.TryGetValue(key!, out var tenant))
                {
                    var locked = tenant.GetAttributeValueOrDefault<bool>("RtBlueprintLocked")
                        ?? true;
                    if (locked)
                    {
                        diff.ToUpdate++;
                        diff.EntitiesToUpdate.Add(seed);
                    }
                    else
                    {
                        var entityId = tenant.RtId.ToString() ?? string.Empty;
                        diff.Conflicts.Add(new BlueprintUpdateConflict
                        {
                            EntityId = entityId,
                            EntityWellKnownName = tenant.RtWellKnownName,
                            EntityCkTypeId = ckTypeId.ToString(),
                            Description = $"Entity '{key}' is unlocked (rtBlueprintLocked=false); skipping in {updateMode} mode",
                            ConflictType = ConflictType.UserModified,
                            SuggestedResolution = ConflictResolution.Skip
                        });
                        diff.ConflictSeedEntities[entityId] = seed;
                    }
                }
                else
                {
                    diff.ToAdd++;
                    diff.EntitiesToAdd.Add(seed);
                }
            }

            if (updateMode == BlueprintUpdateMode.Full)
            {
                foreach (var kvp in tenantOfBlueprintByKey)
                {
                    if (seedKeys.Contains(kvp.Key))
                    {
                        continue;
                    }

                    var tenantEntity = kvp.Value;
                    var locked = tenantEntity.GetAttributeValueOrDefault<bool>("RtBlueprintLocked")
                        ?? true;
                    if (locked)
                    {
                        diff.ToDelete++;
                        diff.EntitiesToDelete.Add(new DeletionTarget(
                            ckTypeId,
                            tenantEntity.RtId,
                            kvp.Key,
                            tenantEntity.RtWellKnownName));
                    }
                    else
                    {
                        var entityId = tenantEntity.RtId.ToString() ?? string.Empty;
                        diff.Conflicts.Add(new BlueprintUpdateConflict
                        {
                            EntityId = entityId,
                            EntityWellKnownName = tenantEntity.RtWellKnownName,
                            EntityCkTypeId = ckTypeId.ToString(),
                            Description = $"Entity '{kvp.Key}' is no longer in seed data but is unlocked; user modifications would block delete",
                            ConflictType = ConflictType.DeleteModified,
                            SuggestedResolution = ConflictResolution.Skip
                        });
                        diff.ConflictDeletionTargets[entityId] = new DeletionTarget(
                            ckTypeId, tenantEntity.RtId, kvp.Key, tenantEntity.RtWellKnownName);
                    }
                }
            }
        }

        return diff;
    }

    private static bool IsSameBlueprintName(string source, BlueprintId target)
    {
        try
        {
            return new BlueprintId(source).Name == target.Name;
        }
        catch
        {
            return false;
        }
    }

    private sealed class BlueprintUpdateDiff
    {
        public int ToAdd { get; set; }
        public int ToUpdate { get; set; }
        public int ToDelete { get; set; }
        public List<BlueprintUpdateConflict> Conflicts { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<RtEntityTcDto> EntitiesToAdd { get; } = [];
        public List<RtEntityTcDto> EntitiesToUpdate { get; } = [];
        public List<DeletionTarget> EntitiesToDelete { get; } = [];

        /// <summary>
        /// Seed entity for a UserModified conflict, keyed by the conflict's
        /// <see cref="BlueprintUpdateConflict.EntityId"/>. Lets
        /// <c>ApplyConflictOverrides</c> promote a KeepBlueprint resolution
        /// by routing the original seed into <see cref="PromotedConflictUpserts"/>.
        /// </summary>
        public Dictionary<string, RtEntityTcDto> ConflictSeedEntities { get; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Deletion target for a DeleteModified conflict, keyed by the
        /// conflict's <see cref="BlueprintUpdateConflict.EntityId"/>. Used to
        /// resurrect the deletion when a KeepBlueprint resolution is
        /// applied (Full mode only).
        /// </summary>
        public Dictionary<string, DeletionTarget> ConflictDeletionTargets { get; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// UserModified conflicts promoted via <see cref="ConflictResolution.KeepBlueprint"/>.
        /// Honoured by the apply path regardless of <see cref="BlueprintUpdateMode"/> —
        /// an explicit per-entity override beats the mode default.
        /// </summary>
        public List<RtEntityTcDto> PromotedConflictUpserts { get; } = [];

        /// <summary>
        /// DeleteModified conflicts promoted via <see cref="ConflictResolution.KeepBlueprint"/>.
        /// Only honoured in <see cref="BlueprintUpdateMode.Full"/>.
        /// </summary>
        public List<DeletionTarget> PromotedConflictDeletions { get; } = [];
    }

    private sealed record DeletionTarget(
        RtCkId<CkTypeId> CkTypeId,
        OctoObjectId RtId,
        string Key,
        string? WellKnownName);

    /// <inheritdoc />
    public async Task<BlueprintUpdateResult> ApplyUpdateAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintUpdateMode updateMode = BlueprintUpdateMode.Merge,
        BlueprintUpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BlueprintUpdateOptions();

        _logger.LogInformation("Applying update to tenant {TenantId} to version {TargetVersion} with mode {UpdateMode}",
            tenantId, targetVersion, updateMode);

        var result = new BlueprintUpdateResult();
        var correlationId = Guid.NewGuid();

        try
        {
            // 1. Get current blueprint info
            var currentInfo = await _blueprintHistory.GetCurrentAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);

            // 2. Preview the update first
            var preview = await PreviewUpdateAsync(tenantId, targetVersion, updateMode, cancellationToken)
                .ConfigureAwait(false);

            // 3. Check for blocking conflicts. A conflict that the caller has
            // pre-resolved (anything other than Skip in ConflictResolutions) is
            // no longer blocking — Phase 2d wires those overrides through the
            // apply path. Skip resolutions and unresolved conflicts still block
            // unless ContinueOnError is set.
            var resolutionMap = options.ConflictResolutions ?? new Dictionary<string, ConflictResolution>();
            var unresolvedConflicts = preview.Conflicts
                .Where(c => !resolutionMap.TryGetValue(c.EntityId, out var r) || r == ConflictResolution.Skip)
                .ToList();

            if (unresolvedConflicts.Count > 0 && !options.ContinueOnError)
            {
                result.Success = false;
                result.Errors.Add("Update blocked by conflicts. Use ContinueOnError to proceed anyway.");
                foreach (var conflict in unresolvedConflicts)
                {
                    result.Errors.Add($"Conflict: {conflict.Description}");
                }
                await NotifyUpdateFailedAsync(tenantId, targetVersion, result.Errors, correlationId).ConfigureAwait(false);
                return result;
            }

            // Handle dry run
            if (options.DryRun)
            {
                _logger.LogInformation("DryRun: Would apply update from {CurrentVersion} to {TargetVersion}",
                    currentInfo?.BlueprintId, targetVersion);

                result.Success = true;
                result.EntitiesAdded = preview.EntitiesToAdd;
                result.EntitiesUpdated = preview.EntitiesToUpdate;
                result.EntitiesDeleted = preview.EntitiesToDelete;
                result.Warnings.Add("DryRun: No changes were made");
                return result;
            }

            // 4. Create backup if requested
            if (options.CreateBackup)
            {
                try
                {
                    var backupReason = $"Before update to {targetVersion.FullName}";
                    var backupInfo = await _backupService.CreateBackupAsync(
                        tenantId, backupReason, cancellationToken)
                        .ConfigureAwait(false);

                    result.BackupId = backupInfo.BackupId;
                    _logger.LogInformation("Created backup {BackupId} before update", backupInfo.BackupId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create backup before update");
                    result.Warnings.Add($"Failed to create backup: {ex.Message}");

                    if (!options.ContinueOnError)
                    {
                        result.Success = false;
                        result.Errors.Add("Backup creation failed and ContinueOnError is false");
                        await NotifyUpdateFailedAsync(tenantId, targetVersion, result.Errors, correlationId).ConfigureAwait(false);
                        return result;
                    }
                }
            }

            // 5. Fetch target blueprint
            var operationResult = new OperationResult();
            var targetBlueprint = await _blueprintCatalogManager
                .GetAsync(targetVersion, operationResult)
                .ConfigureAwait(false);

            if (operationResult.HasErrors)
            {
                result.Success = false;
                foreach (var error in operationResult.Messages.Where(m => m.MessageLevel == MessageLevel.Error))
                {
                    result.Errors.Add(error.MessageText);
                }
                await NotifyUpdateFailedAsync(tenantId, targetVersion, result.Errors, correlationId).ConfigureAwait(false);
                return result;
            }

            var ckDependencies = targetBlueprint.CkModelDependencies ?? [];

            // 5b. Execute CK model migrations if upgrading from a previous version
            if (ckDependencies.Count > 0)
            {
                var ckMigrationOptions = new CkMigrationOptions
                {
                    CreateBackup = options.CreateBackup,
                    DryRun = options.DryRun,
                    ContinueOnError = options.ContinueOnError
                };

                var upgradeResult = await _ckModelUpgradeService.UpgradeModelsAsync(
                    tenantId, ckDependencies, ckMigrationOptions, null, cancellationToken)
                    .ConfigureAwait(false);

                if (!upgradeResult.Success)
                {
                    result.Success = false;
                    result.Errors.AddRange(upgradeResult.Errors.Select(e => $"CK model migration: {e}"));
                    await NotifyUpdateFailedAsync(tenantId, targetVersion, result.Errors, correlationId).ConfigureAwait(false);
                    return result;
                }

                result.Warnings.AddRange(upgradeResult.Warnings.Select(w => $"CK model migration: {w}"));

                _logger.LogInformation(
                    "CK model upgrades completed during update: {Upgraded} upgraded, {TotalAffected} entities affected",
                    upgradeResult.UpgradedModels.Count,
                    upgradeResult.TotalEntitiesAffected);
            }

            // 6. Apply update based on mode
            if (updateMode == BlueprintUpdateMode.Migration)
            {
                // Execute migration script
                var migrationRef = (targetBlueprint.Migrations ?? [])
                    .FirstOrDefault(m => m.FromVersion == currentInfo?.BlueprintId.Version.ToString());

                if (migrationRef != null)
                {
                    var blueprintPath = await _blueprintCatalogManager
                        .GetBlueprintPathAsync(targetVersion)
                        .ConfigureAwait(false);

                    var migrationPath = Path.Combine(blueprintPath, migrationRef.ScriptPath);
                    var migration = await _migrationParser.ParseAsync(migrationPath, cancellationToken)
                        .ConfigureAwait(false);

                    var migrationOptions = new BlueprintMigrationExecutionOptions
                    {
                        DryRun = options.DryRun,
                        ContinueOnError = options.ContinueOnError,
                        CreateBackup = options.CreateBackup,
                        BlueprintSource = targetVersion.FullName
                    };

                    var migrationResult = await _migrationExecutor.ExecuteAsync(
                        tenantId, migration, migrationOptions, cancellationToken)
                        .ConfigureAwait(false);

                    result.Success = migrationResult.Success;
                    result.EntitiesAdded = migrationResult.EntitiesAdded;
                    result.EntitiesUpdated = migrationResult.EntitiesUpdated;
                    result.EntitiesDeleted = migrationResult.EntitiesDeleted;
                    result.EntitiesSkipped = migrationResult.SkippedSteps;
                    result.Errors.AddRange(migrationResult.Errors);
                    result.Warnings.AddRange(migrationResult.Warnings);
                    result.BackupId = migrationResult.BackupId;
                }
                else
                {
                    result.Warnings.Add("No migration script found - applying seed data with Merge mode");
                    await ApplyDiffAsync(tenantId, targetVersion, targetBlueprint,
                        BlueprintUpdateMode.Merge, options, result, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                // Mode-specific apply against the diff
                await ApplyDiffAsync(tenantId, targetVersion, targetBlueprint,
                    updateMode, options, result, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 7. Record update in history
            if (result.Success)
            {
                var blueprintInfo = new TenantBlueprintInfo
                {
                    BlueprintId = targetVersion,
                    AppliedAt = DateTime.UtcNow,
                    ApplicationMode = updateMode == BlueprintUpdateMode.Migration
                        ? BlueprintApplicationMode.Migration
                        : BlueprintApplicationMode.Update,
                    PreviousVersion = currentInfo?.BlueprintId,
                    EntitiesCreated = result.EntitiesAdded,
                    EntitiesUpdated = result.EntitiesUpdated,
                    EntitiesDeleted = result.EntitiesDeleted
                };

                await _blueprintHistory.AddEntryAsync(tenantId, blueprintInfo, cancellationToken)
                    .ConfigureAwait(false);

                result.NewBlueprintInfo = blueprintInfo;

                _logger.LogInformation(
                    "Update applied successfully: {Added} added, {Updated} updated, {Deleted} deleted",
                    result.EntitiesAdded, result.EntitiesUpdated, result.EntitiesDeleted);

                await _notifications.NotifyUpdatedAsync(
                    new BlueprintUpdatedNotification(
                        tenantId,
                        targetVersion,
                        currentInfo?.BlueprintId,
                        updateMode,
                        result.EntitiesAdded,
                        result.EntitiesUpdated,
                        result.EntitiesDeleted,
                        result.BackupId,
                        correlationId,
                        DateTime.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await NotifyUpdateFailedAsync(tenantId, targetVersion, result.Errors, correlationId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying update to tenant {TenantId}", tenantId);
            result.Success = false;
            result.Errors.Add($"Failed to apply update: {ex.Message}");
            await NotifyUpdateFailedAsync(tenantId, targetVersion, result.Errors, correlationId).ConfigureAwait(false);
        }

        return result;
    }

    private async Task NotifyUpdateFailedAsync(
        string tenantId,
        BlueprintId targetVersion,
        IReadOnlyList<string> errors,
        Guid correlationId)
    {
        try
        {
            await _notifications.NotifyOperationFailedAsync(
                new BlueprintOperationFailedNotification(
                    tenantId,
                    targetVersion,
                    Operation: "Update",
                    ErrorMessage: string.Join("; ", errors),
                    correlationId,
                    DateTime.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(notifyEx, "Failed to publish BlueprintOperationFailed notification (Update)");
        }
    }

    /// <summary>
    /// Mode-specific write path used by <see cref="ApplyUpdateAsync"/>. Computes
    /// a diff (same logic as the preview) and applies the resulting buckets:
    /// <list type="bullet">
    /// <item><description>Safe — adds only entities not yet in the tenant.</description></item>
    /// <item><description>Merge — adds new entities and upserts locked entities.</description></item>
    /// <item><description>Full — adds, updates, and deletes orphan locked entities.</description></item>
    /// </list>
    /// Per-entity conflict resolutions from <paramref name="options"/> can promote
    /// otherwise-skipped unlocked conflicts back into the apply set (KeepBlueprint).
    /// </summary>
    private async Task ApplyDiffAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintMetaRootDto targetBlueprint,
        BlueprintUpdateMode mode,
        BlueprintUpdateOptions options,
        BlueprintUpdateResult result,
        CancellationToken cancellationToken)
    {
        var repository = await _runtimeRepositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (repository == null)
        {
            result.Errors.Add($"No runtime repository available for tenant {tenantId}");
            result.Success = false;
            return;
        }

        var diff = await ComputeUpdateDiffAsync(
            tenantId, targetVersion, targetBlueprint, mode, cancellationToken)
            .ConfigureAwait(false);

        result.Warnings.AddRange(diff.Warnings);

        // Apply user-supplied conflict resolutions to override the default Skip.
        var conflictOverrides = options.ConflictResolutions ?? new Dictionary<string, ConflictResolution>();
        ApplyConflictOverrides(diff, conflictOverrides, mode, result);

        // Bucket counts after overrides — mode-driven counts plus promoted
        // conflicts, which always apply regardless of mode (an explicit per-
        // entity override beats the mode default).
        var modeUpserts = mode == BlueprintUpdateMode.Safe ? 0 : diff.EntitiesToUpdate.Count;
        var modeDeletes = mode == BlueprintUpdateMode.Full ? diff.EntitiesToDelete.Count : 0;
        result.EntitiesAdded += diff.EntitiesToAdd.Count;
        result.EntitiesUpdated += modeUpserts + diff.PromotedConflictUpserts.Count;
        result.EntitiesDeleted += modeDeletes + diff.PromotedConflictDeletions.Count;
        result.EntitiesSkipped += diff.Conflicts.Count(c => c.SuggestedResolution == ConflictResolution.Skip);

        // Build the import set: Safe = Add only; Merge/Full = Add + Update.
        // Promoted KeepBlueprint upserts apply on top regardless of mode.
        var entitiesToImport = new List<RtEntityTcDto>(diff.EntitiesToAdd);
        if (mode != BlueprintUpdateMode.Safe)
        {
            entitiesToImport.AddRange(diff.EntitiesToUpdate);
        }
        entitiesToImport.AddRange(diff.PromotedConflictUpserts);

        if (entitiesToImport.Count > 0)
        {
            try
            {
                var importRoot = new RtModelRootTcDto
                {
                    Dependencies = targetBlueprint.CkModelDependencies?.ToList() ?? [],
                    Entities = entitiesToImport
                };

                await _importRtModelCommand.ImportModelAsync(
                    repository,
                    importRoot,
                    ImportStrategy.Upsert,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing blueprint entities for {BlueprintId}", targetVersion);
                result.Errors.Add($"Failed to import blueprint entities: {ex.Message}");
                if (!options.ContinueOnError)
                {
                    result.Success = false;
                    return;
                }
            }
        }

        // Deletions: Full-mode orphan deletes plus any DeleteModified
        // conflicts the caller explicitly promoted via KeepBlueprint.
        var deletions = new List<DeletionTarget>();
        if (mode == BlueprintUpdateMode.Full)
        {
            deletions.AddRange(diff.EntitiesToDelete);
        }
        deletions.AddRange(diff.PromotedConflictDeletions);

        if (deletions.Count > 0)
        {
            await DeleteOrphanEntitiesAsync(repository, deletions, options, result, cancellationToken)
                .ConfigureAwait(false);
        }

        result.Success = result.Errors.Count == 0;
    }

    private static void ApplyConflictOverrides(
        BlueprintUpdateDiff diff,
        Dictionary<string, ConflictResolution> resolutions,
        BlueprintUpdateMode mode,
        BlueprintUpdateResult result)
    {
        if (resolutions.Count == 0)
        {
            return;
        }

        foreach (var conflict in diff.Conflicts.ToList())
        {
            if (!resolutions.TryGetValue(conflict.EntityId, out var resolution))
            {
                continue;
            }

            if (resolution != ConflictResolution.KeepBlueprint)
            {
                // KeepUser / Skip / Merge — leave the conflict in place, no action.
                conflict.SuggestedResolution = resolution;
                continue;
            }

            // Promote: an unlocked update/delete now goes through.
            conflict.SuggestedResolution = ConflictResolution.KeepBlueprint;

            switch (conflict.ConflictType)
            {
                case ConflictType.UserModified:
                    if (diff.ConflictSeedEntities.TryGetValue(conflict.EntityId, out var seed))
                    {
                        // Re-lock so the next diff sees this as a normal locked update
                        // instead of conflicting again, then push into the apply set.
                        // The seed already carries the new blueprint source + applied-at
                        // stamps from LoadAndTagSeedAsync.
                        SetOrReplaceAttribute(seed, RtBlueprintLockedAttrId, true);
                        diff.PromotedConflictUpserts.Add(seed);
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"KeepBlueprint override on UserModified conflict for '{conflict.EntityId}' " +
                            "could not be applied: seed entity was not captured during diff");
                    }
                    break;
                case ConflictType.DeleteModified when mode == BlueprintUpdateMode.Full:
                    if (diff.ConflictDeletionTargets.TryGetValue(conflict.EntityId, out var target))
                    {
                        diff.PromotedConflictDeletions.Add(target);
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"KeepBlueprint override on DeleteModified conflict for '{conflict.EntityId}' " +
                            "could not be applied: deletion target was not captured during diff");
                    }
                    break;
                case ConflictType.DeleteModified:
                    result.Warnings.Add(
                        $"KeepBlueprint override on DeleteModified conflict for '{conflict.EntityId}' " +
                        $"requires Full mode; current mode is {mode}");
                    break;
            }
        }
    }

    private async Task DeleteOrphanEntitiesAsync(
        IRuntimeRepository repository,
        List<DeletionTarget> targets,
        BlueprintUpdateOptions options,
        BlueprintUpdateResult result,
        CancellationToken cancellationToken)
    {
        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await repository.DeleteOneRtEntityByRtIdAsync(
                    session,
                    target.CkTypeId,
                    target.RtId,
                    DeleteOptions.Erase).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to delete orphan blueprint entity {RtId} ({Key})",
                    target.RtId, target.Key);
                result.Errors.Add(
                    $"Failed to delete orphan entity '{target.WellKnownName ?? target.Key}': {ex.Message}");

                if (!options.ContinueOnError)
                {
                    return;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantBlueprintInfo>> GetHistoryAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting blueprint history for tenant {TenantId}", tenantId);

        return await _blueprintHistory.GetHistoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BackupRestoreResult> RollbackAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid();

        _logger.LogInformation("Rolling back tenant {TenantId} to backup {BackupId}", tenantId, backupId);

        // Capture the active blueprint id BEFORE the restore so the notification carries it.
        var currentInfo = await _blueprintHistory.GetCurrentAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        BackupRestoreResult result;
        try
        {
            result = await _backupService.RestoreBackupAsync(tenantId, backupId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for tenant {TenantId} backup {BackupId}", tenantId, backupId);
            await NotifyRollbackFailedAsync(tenantId, currentInfo?.BlueprintId, ex.Message, correlationId)
                .ConfigureAwait(false);
            throw;
        }

        if (result.Success)
        {
            await _notifications.NotifyRolledBackAsync(
                new BlueprintRolledBackNotification(
                    tenantId,
                    currentInfo?.BlueprintId,
                    backupId,
                    correlationId,
                    DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await NotifyRollbackFailedAsync(
                tenantId,
                currentInfo?.BlueprintId,
                string.Join("; ", result.Errors),
                correlationId).ConfigureAwait(false);
        }

        return result;
    }

    private async Task NotifyRollbackFailedAsync(
        string tenantId,
        BlueprintId? blueprintId,
        string errorMessage,
        Guid correlationId)
    {
        try
        {
            await _notifications.NotifyOperationFailedAsync(
                new BlueprintOperationFailedNotification(
                    tenantId,
                    blueprintId,
                    Operation: "Rollback",
                    ErrorMessage: errorMessage,
                    correlationId,
                    DateTime.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(notifyEx, "Failed to publish BlueprintOperationFailed notification (Rollback)");
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintUninstallResult> UninstallAsync(
        string tenantId,
        string blueprintName,
        bool cascade = false,
        CancellationToken cancellationToken = default)
    {
        var result = new BlueprintUninstallResult();
        var correlationId = Guid.NewGuid();

        _logger.LogInformation(
            "Uninstalling blueprint '{BlueprintName}' from tenant {TenantId} (cascade={Cascade})",
            blueprintName, tenantId, cascade);

        var installation = await _installations
            .GetByBlueprintNameAsync(tenantId, blueprintName, cancellationToken)
            .ConfigureAwait(false);

        if (installation == null)
        {
            result.Success = false;
            result.Errors.Add($"Blueprint '{blueprintName}' is not installed on tenant '{tenantId}'.");
            await NotifyUninstallFailedAsync(tenantId, null, result.Errors, correlationId).ConfigureAwait(false);
            return result;
        }

        result.UninstalledBlueprintId = installation.BlueprintId;

        // Refcount: any other installation that lists this blueprint as a dep blocks uninstall.
        var allInstalled = await _installations.GetInstalledAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        var dependents = allInstalled
            .Where(other => other.BlueprintId.Name != blueprintName
                && other.ResolvedDependencies.Any(d => d.Name == blueprintName))
            .Select(other => other.BlueprintId)
            .ToList();

        if (dependents.Count > 0 && !cascade)
        {
            result.Success = false;
            result.BlockingDependents = dependents;
            result.Errors.Add(
                $"Cannot uninstall '{blueprintName}': still required by " +
                string.Join(", ", dependents.Select(d => d.FullName)) +
                ". Re-run with cascade to remove dependents as well.");
            await NotifyUninstallFailedAsync(
                tenantId, installation.BlueprintId, result.Errors, correlationId)
                .ConfigureAwait(false);
            return result;
        }

        // Cascade: uninstall the dependents first (recursive).
        if (cascade)
        {
            foreach (var dependentId in dependents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var depResult = await UninstallAsync(
                        tenantId, dependentId.Name, cascade: true, cancellationToken)
                    .ConfigureAwait(false);
                if (!depResult.Success)
                {
                    result.Success = false;
                    result.Errors.AddRange(depResult.Errors);
                    await NotifyUninstallFailedAsync(
                        tenantId, installation.BlueprintId, result.Errors, correlationId)
                        .ConfigureAwait(false);
                    return result;
                }
                result.EntitiesDeleted += depResult.EntitiesDeleted;
                result.CascadedDependencies.Add(dependentId);
                result.CascadedDependencies.AddRange(depResult.CascadedDependencies);
            }
        }

        // Remove the entities owned by this blueprint.
        try
        {
            var deleted = await DeleteBlueprintOwnedEntitiesAsync(
                    tenantId, installation.BlueprintId, result, cancellationToken)
                .ConfigureAwait(false);
            result.EntitiesDeleted += deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting entities for blueprint {BlueprintId} on tenant {TenantId}",
                installation.BlueprintId, tenantId);
            result.Success = false;
            result.Errors.Add($"Failed to delete blueprint entities: {ex.Message}");
            await NotifyUninstallFailedAsync(
                tenantId, installation.BlueprintId, result.Errors, correlationId)
                .ConfigureAwait(false);
            return result;
        }

        await _installations.RemoveAsync(tenantId, blueprintName, cancellationToken).ConfigureAwait(false);

        // Cascade transitive deps that are now orphaned and originally came in as dependencies.
        if (cascade)
        {
            foreach (var depBpId in installation.ResolvedDependencies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var depRow = await _installations
                    .GetByBlueprintNameAsync(tenantId, depBpId.Name, cancellationToken)
                    .ConfigureAwait(false);
                if (depRow == null || !depRow.IsDependency)
                {
                    // Either already removed or originally an explicit install — leave it alone.
                    continue;
                }

                // Is anyone else still referencing this dep?
                var remaining = await _installations
                    .GetInstalledAsync(tenantId, cancellationToken).ConfigureAwait(false);
                var stillReferenced = remaining.Any(other =>
                    other.BlueprintId.Name != depBpId.Name
                    && other.ResolvedDependencies.Any(d => d.Name == depBpId.Name));

                if (stillReferenced)
                {
                    continue;
                }

                var cascadeResult = await UninstallAsync(
                        tenantId, depBpId.Name, cascade: true, cancellationToken)
                    .ConfigureAwait(false);
                if (cascadeResult.Success)
                {
                    result.CascadedDependencies.Add(depBpId);
                    result.CascadedDependencies.AddRange(cascadeResult.CascadedDependencies);
                    result.EntitiesDeleted += cascadeResult.EntitiesDeleted;
                }
                else
                {
                    result.Warnings.AddRange(cascadeResult.Errors
                        .Select(e => $"Cascade of '{depBpId.FullName}' failed: {e}"));
                }
            }
        }

        result.Success = result.Errors.Count == 0;

        if (result.Success)
        {
            try
            {
                await _notifications.NotifyUninstalledAsync(
                    new BlueprintUninstalledNotification(
                        tenantId,
                        installation.BlueprintId,
                        result.CascadedDependencies,
                        correlationId,
                        DateTime.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx,
                    "Failed to publish BlueprintUninstalled notification for {BlueprintId}",
                    installation.BlueprintId);
            }
        }

        return result;
    }

    private async Task NotifyUninstallFailedAsync(
        string tenantId,
        BlueprintId? blueprintId,
        IReadOnlyList<string> errors,
        Guid correlationId)
    {
        try
        {
            await _notifications.NotifyOperationFailedAsync(
                new BlueprintOperationFailedNotification(
                    tenantId,
                    blueprintId,
                    Operation: "Uninstall",
                    ErrorMessage: string.Join("; ", errors),
                    correlationId,
                    DateTime.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(notifyEx, "Failed to publish BlueprintOperationFailed notification (Uninstall)");
        }
    }

    /// <summary>
    /// Erases all locked entities owned by the blueprint. Unlocked entities
    /// are left in place — they may carry user modifications, so removing them
    /// silently would be data loss; they are reported as warnings instead.
    /// </summary>
    /// <remarks>
    /// We do not have a global "scan every collection" primitive, so we
    /// re-read the blueprint's seed data and use that as the index of which
    /// CK types this blueprint owns. The matching tenant rows are then
    /// filtered to <c>rtBlueprintSource = blueprint.FullName</c> before
    /// deletion to avoid clobbering rows that other blueprints adopted.
    /// </remarks>
    private async Task<int> DeleteBlueprintOwnedEntitiesAsync(
        string tenantId,
        BlueprintId blueprintId,
        BlueprintUninstallResult result,
        CancellationToken cancellationToken)
    {
        var opResult = new OperationResult();
        var catalogBlueprint = await _blueprintCatalogManager
            .GetAsync(blueprintId, opResult).ConfigureAwait(false);
        if (opResult.HasErrors || string.IsNullOrEmpty(catalogBlueprint.SeedDataPath))
        {
            // No seed data → nothing to erase.
            return 0;
        }

        var blueprintPath = await _blueprintCatalogManager
            .GetBlueprintPathAsync(blueprintId).ConfigureAwait(false);
        var seedDataPath = Path.Combine(blueprintPath, catalogBlueprint.SeedDataPath);

        if (!File.Exists(seedDataPath))
        {
            return 0;
        }

        var seedRoot = await LoadAndTagSeedAsync(seedDataPath, blueprintId, opResult)
            .ConfigureAwait(false);
        if (seedRoot == null)
        {
            result.Warnings.AddRange(opResult.Messages
                .Where(m => m.MessageLevel == MessageLevel.Error)
                .Select(m => m.MessageText));
            return 0;
        }

        var repository = await _runtimeRepositoryProvider
            .GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (repository == null)
        {
            throw new InvalidOperationException(
                $"No runtime repository available for tenant '{tenantId}'");
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        var deletedCount = 0;

        foreach (var typeGroup in seedRoot.Entities.GroupBy(e => e.CkTypeId))
        {
            var ckTypeId = typeGroup.Key;

            IResultSet<RtEntity> tenantEntities;
            try
            {
                tenantEntities = await repository.GetRtEntitiesByTypeAsync(
                    session, ckTypeId, RtEntityQueryOptions.Create()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Warnings.Add(
                    $"Could not query tenant entities for type {ckTypeId}: {ex.Message}");
                continue;
            }

            var seedKeys = new HashSet<string>(
                typeGroup
                    .Select(e => e.RtWellKnownName ?? e.RtId.ToString())
                    .Where(k => !string.IsNullOrEmpty(k))!,
                StringComparer.Ordinal);

            foreach (var entity in tenantEntities.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = entity.RtWellKnownName ?? entity.RtId.ToString();
                if (string.IsNullOrEmpty(key) || !seedKeys.Contains(key!))
                {
                    continue;
                }

                var source = entity.GetAttributeStringValueOrDefault("RtBlueprintSource");
                if (source != blueprintId.FullName)
                {
                    // Adopted by another blueprint — leave it alone.
                    continue;
                }

                var locked = entity.GetAttributeValueOrDefault<bool>("RtBlueprintLocked") ?? true;
                if (!locked)
                {
                    result.Warnings.Add(
                        $"Entity '{key}' (type {ckTypeId}) is unlocked; keeping as user data.");
                    continue;
                }

                try
                {
                    await repository.DeleteOneRtEntityByRtIdAsync(
                        session, ckTypeId, entity.RtId, DeleteOptions.Erase).ConfigureAwait(false);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add(
                        $"Failed to delete entity '{key}' ({ckTypeId}): {ex.Message}");
                }
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// Imports the seed-data file (if any) of one blueprint in an install
    /// order. Returns the number of entities written. On error, errors are
    /// pushed onto <paramref name="operationResult"/> and the caller treats
    /// the whole apply as failed.
    /// </summary>
    private async Task<int> ApplySeedDataForBlueprintAsync(
        string tenantId,
        BlueprintMetaRootDto blueprint,
        OperationResult operationResult,
        List<string> appliedSeedDataFiles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(blueprint.SeedDataPath))
        {
            return 0;
        }

        try
        {
            var blueprintPath = await _blueprintCatalogManager
                .GetBlueprintPathAsync(blueprint.BlueprintId)
                .ConfigureAwait(false);
            var seedDataPath = Path.Combine(blueprintPath, blueprint.SeedDataPath);

            if (!File.Exists(seedDataPath))
            {
                _logger.LogWarning("Seed data file not found: {Path}", seedDataPath);
                operationResult.AddMessage(new OperationMessage(
                    MessageLevel.Warning, seedDataPath, 20, "Seed data file not found"));
                return 0;
            }

            var repository = await _runtimeRepositoryProvider
                .GetRepositoryAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (repository == null)
            {
                operationResult.AddMessage(new OperationMessage(
                    MessageLevel.Error, seedDataPath, 22,
                    $"No runtime repository available for tenant {tenantId}"));
                return 0;
            }

            var seedRoot = await LoadAndTagSeedAsync(seedDataPath, blueprint.BlueprintId, operationResult)
                .ConfigureAwait(false);
            if (seedRoot == null)
            {
                return 0;
            }

            await _importRtModelCommand.ImportModelAsync(
                repository, seedRoot, ImportStrategy.Upsert, cancellationToken).ConfigureAwait(false);

            appliedSeedDataFiles.Add(seedDataPath);
            return seedRoot.Entities.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying seed data for blueprint {BlueprintId}",
                blueprint.BlueprintId);
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error, blueprint.SeedDataPath, 21,
                $"Error applying seed data: {ex.Message}"));
            return 0;
        }
    }

    /// <summary>
    /// Reads a seed-data YAML file from disk, deserialises it and stamps the
    /// blueprint provenance attributes on every entity. Returns null and emits
    /// errors via <paramref name="operationResult"/> when the file cannot be parsed.
    /// </summary>
    private async Task<RtModelRootTcDto?> LoadAndTagSeedAsync(
        string seedDataPath,
        BlueprintId blueprintId,
        OperationResult operationResult)
    {
        try
        {
            using var stream = File.OpenRead(seedDataPath);
            var root = await _rtYamlSerializer
                .DeserializeAsync(stream, seedDataPath, operationResult)
                .ConfigureAwait(false);

            if (operationResult.HasErrors)
            {
                return null;
            }

            StampBlueprintTags(root, blueprintId, DateTime.UtcNow);
            return root;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialise seed data from {Path}", seedDataPath);
            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error,
                seedDataPath,
                23,
                $"Failed to deserialise seed data: {ex.Message}"));
            return null;
        }
    }
}
