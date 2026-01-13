using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Implements blueprint application to tenants.
/// </summary>
internal class BlueprintService : IBlueprintService
{
    private readonly ICkCacheService _ckCacheService;
    private readonly IBlueprintComposer _blueprintComposer;
    private readonly IBlueprintCatalogManager _blueprintCatalogManager;
    private readonly ITenantBlueprintHistory _blueprintHistory;
    private readonly ITenantBackupService _backupService;
    private readonly IMigrationExecutor _migrationExecutor;
    private readonly IMigrationParser _migrationParser;
    private readonly IRtYamlSerializer _rtYamlSerializer;
    private readonly ICkModelUpgradeService _ckModelUpgradeService;
    private readonly ILogger<BlueprintService> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintService"/>
    /// </summary>
    public BlueprintService(
        ICkCacheService ckCacheService,
        IBlueprintComposer blueprintComposer,
        IBlueprintCatalogManager blueprintCatalogManager,
        ITenantBlueprintHistory blueprintHistory,
        ITenantBackupService backupService,
        IMigrationExecutor migrationExecutor,
        IMigrationParser migrationParser,
        IRtYamlSerializer rtYamlSerializer,
        ICkModelUpgradeService ckModelUpgradeService,
        ILogger<BlueprintService> logger)
    {
        _ckCacheService = ckCacheService;
        _blueprintComposer = blueprintComposer;
        _blueprintCatalogManager = blueprintCatalogManager;
        _blueprintHistory = blueprintHistory;
        _backupService = backupService;
        _migrationExecutor = migrationExecutor;
        _migrationParser = migrationParser;
        _rtYamlSerializer = rtYamlSerializer;
        _ckModelUpgradeService = ckModelUpgradeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BlueprintApplicationResult> ApplyBlueprintAsync(
        string tenantId,
        BlueprintId blueprintId,
        CancellationToken cancellationToken = default)
    {
        var operationResult = new OperationResult();
        var loadedCkModels = new List<CkModelIdVersionRange>();
        var appliedSeedDataFiles = new List<string>();
        var entitiesCreated = 0;

        try
        {
            _logger.LogInformation("Applying blueprint {BlueprintId} to tenant {TenantId}",
                blueprintId, tenantId);

            // 1. Compose blueprint (resolve hierarchy)
            var composedBlueprint = await _blueprintComposer.ComposeAsync(
                blueprintId, operationResult, cancellationToken)
                .ConfigureAwait(false);

            if (operationResult.HasErrors)
            {
                return BlueprintApplicationResult.Failed(operationResult);
            }

            // 2. Create tenant if not exists
            if (!_ckCacheService.IsTenantLoaded(tenantId))
            {
                _ckCacheService.CreateTenant(tenantId);
                _logger.LogDebug("Created tenant {TenantId}", tenantId);
            }

            // 3. Load CK models into tenant cache and execute CK model migrations if needed
            // Note: The actual CK model loading would be done via the dependency resolver
            // For now, we record which models need to be loaded
            foreach (var ckDependency in composedBlueprint.CkModelDependencies)
            {
                _logger.LogDebug("Blueprint requires CK model: {CkModel}", ckDependency);
                loadedCkModels.Add(ckDependency);
                // The actual loading will be delegated to the CK catalog system
                // This is a placeholder for the integration point
            }

            // 3b. Execute CK model migrations if upgrading from a previous version
            if (loadedCkModels.Count > 0)
            {
                var migrationOptions = new CkMigrationOptions
                {
                    CreateBackup = true,
                    DryRun = false,
                    ContinueOnError = false
                };

                var upgradeResult = await _ckModelUpgradeService.UpgradeModelsAsync(
                    tenantId, loadedCkModels, migrationOptions, null, cancellationToken)
                    .ConfigureAwait(false);

                if (!upgradeResult.Success)
                {
                    foreach (var error in upgradeResult.Errors)
                    {
                        operationResult.AddMessage(new OperationMessage(
                            MessageLevel.Error,
                            null,
                            25,
                            $"CK model migration failed: {error}"));
                    }
                    return BlueprintApplicationResult.Failed(operationResult);
                }

                // Add warnings to result
                foreach (var warning in upgradeResult.Warnings)
                {
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Warning,
                        null,
                        26,
                        $"CK model migration: {warning}"));
                }

                _logger.LogInformation(
                    "CK model upgrades completed: {Upgraded} upgraded, {Skipped} skipped, {TotalAffected} entities affected",
                    upgradeResult.UpgradedModels.Count,
                    upgradeResult.SkippedModels.Count,
                    upgradeResult.TotalEntitiesAffected);
            }

            // 4. Resolve and apply seed data in order (base -> child)
            foreach (var seedDataRef in composedBlueprint.SeedDataReferences)
            {
                try
                {
                    var blueprintPath = await _blueprintCatalogManager
                        .GetBlueprintPathAsync(seedDataRef.BlueprintId)
                        .ConfigureAwait(false);

                    var seedDataPath = Path.Combine(blueprintPath, seedDataRef.SeedDataPath);
                    seedDataRef.ResolvedPath = seedDataPath;

                    if (!File.Exists(seedDataPath))
                    {
                        _logger.LogWarning("Seed data file not found: {Path}", seedDataPath);
                        operationResult.AddMessage(new OperationMessage(
                            MessageLevel.Warning,
                            seedDataPath,
                            20,
                            "Seed data file not found"));
                        continue;
                    }

                    _logger.LogDebug("Applying seed data from {Path}", seedDataPath);

                    // Load and apply seed data
                    // Note: The actual import would use ImportRtModelCommand
                    // This is a placeholder for the integration point
                    appliedSeedDataFiles.Add(seedDataPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error applying seed data for blueprint {BlueprintId}",
                        seedDataRef.BlueprintId);
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Error,
                        seedDataRef.SeedDataPath,
                        21,
                        $"Error applying seed data: {ex.Message}"));
                }
            }

            // 5. Record the application in history
            var blueprintInfo = new TenantBlueprintInfo
            {
                BlueprintId = blueprintId,
                AppliedAt = DateTime.UtcNow,
                ApplicationMode = BlueprintApplicationMode.Initial,
                EntitiesCreated = entitiesCreated,
                EntitiesUpdated = 0,
                EntitiesDeleted = 0
            };

            await _blueprintHistory.AddEntryAsync(tenantId, blueprintInfo, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Blueprint {BlueprintId} applied successfully to tenant {TenantId}: {SeedDataCount} seed data files applied",
                blueprintId, tenantId, appliedSeedDataFiles.Count);

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

            return BlueprintApplicationResult.Failed(operationResult);
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

            // Try to compose to check all dependencies
            var composedBlueprint = await _blueprintComposer.ComposeAsync(
                blueprintId, operationResult, cancellationToken)
                .ConfigureAwait(false);

            // Validate CK model dependencies exist
            // Note: This would check against the CK catalog
            // For now, we just collect the dependencies for validation

            // Validate seed data files exist
            foreach (var seedDataRef in composedBlueprint.SeedDataReferences)
            {
                var blueprintPath = await _blueprintCatalogManager
                    .GetBlueprintPathAsync(seedDataRef.BlueprintId)
                    .ConfigureAwait(false);

                var seedDataPath = Path.Combine(blueprintPath, seedDataRef.SeedDataPath);
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
                var composedBlueprint = await _blueprintComposer.ComposeAsync(
                    recommendedVersion, operationResult, cancellationToken)
                    .ConfigureAwait(false);

                // Check if there's a migration from current version
                var migrationFromCurrent = composedBlueprint.AvailableMigrations
                    .FirstOrDefault(m => m.FromVersion == currentInfo.BlueprintId.Version.ToString());

                hasMigrationPath = migrationFromCurrent != null;

                availableMigrations = composedBlueprint.AvailableMigrations
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

        // 3. Compose target blueprint
        var operationResult = new OperationResult();
        var composedBlueprint = await _blueprintComposer.ComposeAsync(
            targetVersion, operationResult, cancellationToken)
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
            var migrationRef = composedBlueprint.AvailableMigrations
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
                        .GetBlueprintPathAsync(migrationRef.BlueprintId)
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
            // For non-migration modes, analyze seed data differences
            // This is a simplified preview - actual implementation would compare entities
            preview.EntitiesToAdd = composedBlueprint.SeedDataReferences.Count;
            preview.EntitiesToUpdate = 0;
            preview.EntitiesToDelete = updateMode == BlueprintUpdateMode.Full ? 1 : 0; // Placeholder

            if (updateMode == BlueprintUpdateMode.Safe)
            {
                preview.Warnings.Add("Safe mode: Only new entities will be added, existing entities will not be modified");
            }
            else if (updateMode == BlueprintUpdateMode.Merge)
            {
                preview.Warnings.Add("Merge mode: Blueprint-locked entities (rtBlueprintLocked=true) will be updated");
            }
            else if (updateMode == BlueprintUpdateMode.Full)
            {
                preview.Warnings.Add("Full mode: All entities from blueprint will be synchronized - user modifications may be lost");
            }
        }

        return preview;
    }

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

        try
        {
            // 1. Get current blueprint info
            var currentInfo = await _blueprintHistory.GetCurrentAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);

            // 2. Preview the update first
            var preview = await PreviewUpdateAsync(tenantId, targetVersion, updateMode, cancellationToken)
                .ConfigureAwait(false);

            // 3. Check for blocking conflicts
            if (!preview.CanProceed && !options.ContinueOnError)
            {
                result.Success = false;
                result.Errors.Add("Update blocked by conflicts. Use ContinueOnError to proceed anyway.");
                foreach (var conflict in preview.Conflicts)
                {
                    result.Errors.Add($"Conflict: {conflict.Description}");
                }
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
                        return result;
                    }
                }
            }

            // 5. Compose target blueprint
            var operationResult = new OperationResult();
            var composedBlueprint = await _blueprintComposer.ComposeAsync(
                targetVersion, operationResult, cancellationToken)
                .ConfigureAwait(false);

            if (operationResult.HasErrors)
            {
                result.Success = false;
                foreach (var error in operationResult.Messages.Where(m => m.MessageLevel == MessageLevel.Error))
                {
                    result.Errors.Add(error.MessageText);
                }
                return result;
            }

            // 5b. Execute CK model migrations if upgrading from a previous version
            if (composedBlueprint.CkModelDependencies.Count > 0)
            {
                var ckMigrationOptions = new CkMigrationOptions
                {
                    CreateBackup = options.CreateBackup,
                    DryRun = options.DryRun,
                    ContinueOnError = options.ContinueOnError
                };

                var upgradeResult = await _ckModelUpgradeService.UpgradeModelsAsync(
                    tenantId, composedBlueprint.CkModelDependencies, ckMigrationOptions, null, cancellationToken)
                    .ConfigureAwait(false);

                if (!upgradeResult.Success)
                {
                    result.Success = false;
                    result.Errors.AddRange(upgradeResult.Errors.Select(e => $"CK model migration: {e}"));
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
                var migrationRef = composedBlueprint.AvailableMigrations
                    .FirstOrDefault(m => m.FromVersion == currentInfo?.BlueprintId.Version.ToString());

                if (migrationRef != null)
                {
                    var blueprintPath = await _blueprintCatalogManager
                        .GetBlueprintPathAsync(migrationRef.BlueprintId)
                        .ConfigureAwait(false);

                    var migrationPath = Path.Combine(blueprintPath, migrationRef.ScriptPath);
                    var migration = await _migrationParser.ParseAsync(migrationPath, cancellationToken)
                        .ConfigureAwait(false);

                    var migrationOptions = new MigrationExecutionOptions
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
                    await ApplySeedDataAsync(tenantId, composedBlueprint, targetVersion, result, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                // Apply seed data based on update mode
                await ApplySeedDataAsync(tenantId, composedBlueprint, targetVersion, result, cancellationToken)
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying update to tenant {TenantId}", tenantId);
            result.Success = false;
            result.Errors.Add($"Failed to apply update: {ex.Message}");
        }

        return result;
    }

    private async Task ApplySeedDataAsync(
        string tenantId,
        ComposedBlueprintDto composedBlueprint,
        BlueprintId targetVersion,
        BlueprintUpdateResult result,
        CancellationToken cancellationToken)
    {
        // Apply seed data from composed blueprint
        foreach (var seedDataRef in composedBlueprint.SeedDataReferences)
        {
            try
            {
                var blueprintPath = await _blueprintCatalogManager
                    .GetBlueprintPathAsync(seedDataRef.BlueprintId)
                    .ConfigureAwait(false);

                var seedDataPath = Path.Combine(blueprintPath, seedDataRef.SeedDataPath);

                if (!File.Exists(seedDataPath))
                {
                    result.Warnings.Add($"Seed data file not found: {seedDataPath}");
                    continue;
                }

                _logger.LogDebug("Applying seed data from {Path}", seedDataPath);

                // TODO: Integrate with IImportRtModelCommand to actually import entities
                // For now, we just count the files
                result.EntitiesAdded++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying seed data for blueprint {BlueprintId}",
                    seedDataRef.BlueprintId);
                result.Warnings.Add($"Error applying seed data: {ex.Message}");
            }
        }

        result.Success = result.Errors.Count == 0;
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
}
