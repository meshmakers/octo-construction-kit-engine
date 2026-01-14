using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Service for executing CK model migrations when upgrading between versions
/// </summary>
internal class CkModelMigrationService : ICkModelMigrationService
{
    private readonly ICkMigrationParser _migrationParser;
    private readonly ITenantBackupService _backupService;
    private readonly ICkMigrationContentProvider _contentProvider;
    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ICatalogService _catalogService;
    private readonly ILogger<CkModelMigrationService> _logger;

    /// <summary>
    /// Well-known attribute names for CK model migration (must match CK model schema - PascalCase)
    /// </summary>
    private const string AttributeCkTypeId = "ckTypeId";
    private const string AttributeCkModelName = "CkModelName";
    private const string AttributeFromVersion = "FromVersion";
    private const string AttributeToVersion = "ToVersion";
    private const string AttributeExecutedAt = "ExecutedAt";
    private const string AttributeSuccess = "Success";
    private const string AttributeDurationMs = "DurationMs";
    private const string AttributeEntitiesAdded = "EntitiesAdded";
    private const string AttributeEntitiesUpdated = "EntitiesUpdated";
    private const string AttributeEntitiesDeleted = "EntitiesDeleted";
    private const string AttributeErrors = "Errors";
    private const string AttributeBackupId = "BackupId";

    /// <summary>
    /// Creates a new instance of <see cref="CkModelMigrationService"/>
    /// </summary>
    public CkModelMigrationService(
        ICkMigrationParser migrationParser,
        ITenantBackupService backupService,
        ICkMigrationContentProvider contentProvider,
        IRuntimeRepositoryProvider repositoryProvider,
        ICatalogService catalogService,
        ILogger<CkModelMigrationService> logger)
    {
        _migrationParser = migrationParser;
        _backupService = backupService;
        _contentProvider = contentProvider;
        _repositoryProvider = repositoryProvider;
        _catalogService = catalogService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CkMigrationResult> MigrateAsync(
        string tenantId,
        CkModelId fromModel,
        CkModelId toModel,
        CkMigrationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CkMigrationOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting CK model migration for tenant {TenantId}: {FromModel} -> {ToModel} (DryRun: {DryRun})",
            tenantId, fromModel, toModel, options.DryRun);

        // Validate models have the same name
        if (fromModel.Name != toModel.Name)
        {
            return CkMigrationResult.Failed(fromModel, toModel,
                $"Cannot migrate between different CK models: {fromModel.Name} and {toModel.Name}");
        }

        // Find migration path
        var migrationPath = await FindMigrationPathAsync(fromModel, toModel, cancellationToken)
            .ConfigureAwait(false);

        if (migrationPath == null)
        {
            return CkMigrationResult.Failed(fromModel, toModel,
                $"No migration path found from {fromModel} to {toModel}");
        }

        var result = CkMigrationResult.Succeeded(fromModel, toModel);

        try
        {
            // Create backup if requested
            if (options is { CreateBackup: true, DryRun: false })
            {
                try
                {
                    var backupReason = $"Before CK migration {fromModel} -> {toModel}";
                    var backupInfo = await _backupService.CreateBackupAsync(tenantId, backupReason, cancellationToken)
                        .ConfigureAwait(false);
                    result.BackupId = backupInfo.BackupId;
                    _logger.LogInformation("Created backup {BackupId} before migration", backupInfo.BackupId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create backup before migration");
                    result.Warnings.Add($"Failed to create backup: {ex.Message}");

                    if (!options.ContinueOnError)
                    {
                        result.Success = false;
                        result.Errors.Add("Backup creation failed and ContinueOnError is false");
                        return result;
                    }
                }
            }

            // Execute each migration step in the path
            foreach (var step in migrationPath.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Executing migration step: {FromVersion} -> {ToVersion}",
                    step.FromVersion, step.ToVersion);

                var stepResult = await ExecuteMigrationStepAsync(
                    tenantId, step, options, cancellationToken)
                    .ConfigureAwait(false);

                result.EntitiesAdded += stepResult.EntitiesAdded;
                result.EntitiesUpdated += stepResult.EntitiesUpdated;
                result.EntitiesDeleted += stepResult.EntitiesDeleted;
                result.Warnings.AddRange(stepResult.Warnings);

                if (!stepResult.Success)
                {
                    result.Errors.AddRange(stepResult.Errors);

                    if (!options.ContinueOnError)
                    {
                        result.Success = false;
                        _logger.LogError("Migration step failed: {FromVersion} -> {ToVersion}",
                            step.FromVersion, step.ToVersion);
                        break;
                    }
                }
            }

            // Record migration in history if successful and not dry run
            if (result.Success && !options.DryRun)
            {
                await RecordMigrationHistoryAsync(tenantId, result, stopwatch.ElapsedMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CK model migration");
            result.Success = false;
            result.Errors.Add($"Migration failed: {ex.Message}");
        }

        stopwatch.Stop();
        result.DurationMs = stopwatch.ElapsedMilliseconds;

        _logger.LogInformation(
            "CK model migration completed: {Success}, {Added} added, {Updated} updated, {Deleted} deleted, {Duration}ms",
            result.Success, result.EntitiesAdded, result.EntitiesUpdated, result.EntitiesDeleted, result.DurationMs);

        return result;
    }

    /// <inheritdoc />
    public async Task<CkMigrationPath?> FindMigrationPathAsync(
        CkModelId fromModel,
        CkModelId toModel,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Finding migration path from {FromModel} to {ToModel}", fromModel, toModel);

        if (fromModel.Name != toModel.Name)
        {
            return null;
        }

        // Check if migrations exist for this model
        if (!await _contentProvider.HasMigrationsAsync(toModel, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("No migrations found for CK model {Model}", toModel);
            return null;
        }

        try
        {
            var meta = await _contentProvider.GetMigrationMetaAsync(toModel, cancellationToken)
                .ConfigureAwait(false);

            if (meta == null)
            {
                _logger.LogDebug("No migration metadata found for {Model}", toModel);
                return null;
            }

            // Find direct migration from source version to exact target version
            var directMigration = meta.Migrations
                .FirstOrDefault(m => m.FromVersion == fromModel.Version.ToString() &&
                                     m.ToVersion == toModel.Version.ToString());

            if (directMigration != null)
            {
                // Load the script directly
                var script = await _contentProvider.GetMigrationAsync(
                    toModel, directMigration.FromVersion, directMigration.ToVersion, cancellationToken)
                    .ConfigureAwait(false);

                return new CkMigrationPath
                {
                    FromModel = fromModel,
                    ToModel = toModel,
                    HasBreakingChanges = directMigration.Breaking,
                    Description = directMigration.Description,
                    Steps =
                    [
                        new CkMigrationStep
                        {
                            FromVersion = directMigration.FromVersion,
                            ToVersion = directMigration.ToVersion,
                            Script = script,
                            Description = directMigration.Description,
                            Breaking = directMigration.Breaking
                        }
                    ]
                };
            }

            // Try to find multi-hop path to exact target version
            var path = await FindMultiHopPathAsync(toModel, meta.Migrations, fromModel.Version.ToString(),
                toModel.Version.ToString(), cancellationToken).ConfigureAwait(false);

            if (path is { Count: > 0 })
            {
                return new CkMigrationPath
                {
                    FromModel = fromModel,
                    ToModel = toModel,
                    HasBreakingChanges = path.Any(s => s.Breaking),
                    Steps = path
                };
            }

            // Fallback: Find a "partial" migration path
            // This handles cases where data migrations are defined for major version jumps (e.g., 1.0.3 -> 2.0.0)
            // but the actual target version is higher (e.g., 2.0.2) due to schema-only changes that don't need data migration.
            // In this case, we execute the available data migration and let the schema update handle the rest.
            var partialPath = await FindPartialMigrationPathAsync(
                toModel, meta.Migrations, fromModel.Version, toModel.Version, cancellationToken)
                .ConfigureAwait(false);

            if (partialPath != null)
            {
                _logger.LogInformation(
                    "Found partial migration path from {FromVersion} to {IntermediateVersion} (target was {ToVersion}). " +
                    "Schema-only changes to {ToVersion} will be applied without data migration.",
                    fromModel.Version, partialPath.ToModel.Version, toModel.Version, toModel.Version);

                return partialPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding migration path");
        }

        return null;
    }

    /// <summary>
    /// Finds a partial migration path that gets as close as possible to the target version.
    /// This handles cases where migrations are defined up to an intermediate version but the
    /// actual target version is higher (due to schema-only changes that don't need data migration).
    /// </summary>
    private async Task<CkMigrationPath?> FindPartialMigrationPathAsync(
        CkModelId toModel,
        IReadOnlyCollection<CkMigrationReferenceDto> migrations,
        CkVersion fromVersion,
        CkVersion targetVersion,
        CancellationToken cancellationToken)
    {
        // Find migrations that start from our source version and go to a version between source and target
        var candidateMigrations = migrations
            .Where(m => m.FromVersion == fromVersion.ToString())
            .Select(m => new { Migration = m, ToVersion = TryParseCkVersion(m.ToVersion) })
            .Where(x => x.ToVersion != null &&
                        x.ToVersion.Value.CompareTo(fromVersion) > 0 &&
                        x.ToVersion.Value.CompareTo(targetVersion) <= 0)
            .OrderByDescending(x => x.ToVersion!.Value)
            .ToList();

        if (candidateMigrations.Count == 0)
        {
            return null;
        }

        // Use the migration that gets us closest to the target
        var bestCandidate = candidateMigrations.First();
        var migration = bestCandidate.Migration;
        var intermediateVersion = bestCandidate.ToVersion!.Value;

        var script = await _contentProvider.GetMigrationAsync(
            toModel, migration.FromVersion, migration.ToVersion, cancellationToken)
            .ConfigureAwait(false);

        var intermediateModel = new CkModelId(toModel.Name, intermediateVersion);

        return new CkMigrationPath
        {
            FromModel = new CkModelId(toModel.Name, fromVersion),
            ToModel = intermediateModel, // Note: This is the intermediate version, not the final target
            HasBreakingChanges = migration.Breaking,
            Description = migration.Description,
            IsPartialPath = true, // Mark this as a partial path
            Steps =
            [
                new CkMigrationStep
                {
                    FromVersion = migration.FromVersion,
                    ToVersion = migration.ToVersion,
                    Script = script,
                    Description = migration.Description,
                    Breaking = migration.Breaking
                }
            ]
        };
    }

    private static CkVersion? TryParseCkVersion(string version)
    {
        try
        {
            return new CkVersion(version);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CkMigrationStatus> GetStatusAsync(
        string tenantId,
        string ckModelName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting migration status for tenant {TenantId}, CK model {CkModelName}",
            tenantId, ckModelName);

        var status = new CkMigrationStatus { CkModelName = ckModelName };

        // Get history to determine installed version
        var history = await GetHistoryAsync(tenantId, ckModelName, cancellationToken)
            .ConfigureAwait(false);

        if (history.Count > 0)
        {
            var lastEntry = history.OrderByDescending(h => h.ExecutedAt).First();
            status.InstalledVersion = lastEntry.ToVersion;
            status.LastMigrationAt = lastEntry.ExecutedAt;
            status.LastMigrationSuccess = lastEntry.Success;
        }

        // Get latest available version from CK catalog
        var latestModel = await GetLatestCkModelVersionAsync(ckModelName, cancellationToken)
            .ConfigureAwait(false);
        if (latestModel != null)
        {
            status.LatestAvailableVersion = latestModel.Version.ToString();

            // Check if migration path exists
            if (status is { InstalledVersion: not null, UpdateAvailable: true })
            {
                var installedModel = new CkModelId(ckModelName, status.InstalledVersion);
                var migrationPath = await FindMigrationPathAsync(installedModel, latestModel, cancellationToken)
                    .ConfigureAwait(false);
                status.MigrationPathAvailable = migrationPath != null;
            }
        }

        return status;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CkMigrationHistoryEntry>> GetHistoryAsync(
        string tenantId,
        string ckModelName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting migration history for tenant {TenantId}, CK model {CkModelName}",
            tenantId, ckModelName);

        var repository = await _repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (repository == null)
        {
            _logger.LogWarning("No repository available for tenant {TenantId}", tenantId);
            return [];
        }

        try
        {
            var session = await repository.GetSessionAsync().ConfigureAwait(false);
            var ckTypeId = new RtCkId<CkTypeId>("System", "MigrationHistory");

            var queryOptions = RtEntityQueryOptions.Create();
            queryOptions.Field(AttributeCkModelName, FieldFilterOperator.Equals, ckModelName);

            var resultSet = await repository.GetRtEntitiesByTypeAsync(session, ckTypeId, queryOptions)
                .ConfigureAwait(false);

            var historyEntries = new List<CkMigrationHistoryEntry>();
            foreach (var entity in resultSet.Items)
            {
                var errorMsgs = entity.GetAttributeStringValuesOrDefault(AttributeErrors);
                historyEntries.Add(new CkMigrationHistoryEntry
                {
                    CkModelName = entity.GetAttributeStringValueOrDefault(AttributeCkModelName) ?? ckModelName,
                    FromVersion = entity.GetAttributeStringValueOrDefault(AttributeFromVersion) ?? "",
                    ToVersion = entity.GetAttributeStringValueOrDefault(AttributeToVersion) ?? "",
                    ExecutedAt = entity.GetAttributeValueOrDefault<DateTime>(AttributeExecutedAt) ?? DateTime.MinValue,
                    Success = entity.GetAttributeValueOrDefault<bool>(AttributeSuccess) ?? false,
                    DurationMs = entity.GetAttributeValueOrDefault<long>(AttributeDurationMs) ?? 0,
                    EntitiesAffected = (entity.GetAttributeValueOrDefault<int>(AttributeEntitiesAdded) ?? 0) +
                                       (entity.GetAttributeValueOrDefault<int>(AttributeEntitiesUpdated) ?? 0) +
                                       (entity.GetAttributeValueOrDefault<int>(AttributeEntitiesDeleted) ?? 0),
                    Errors = errorMsgs?.ToList()
                });
            }

            return historyEntries.OrderByDescending(h => h.ExecutedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving migration history for tenant {TenantId}", tenantId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<CkMigrationValidationResult> ValidateAsync(
        string tenantId,
        CkModelId fromModel,
        CkModelId toModel,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating migration from {FromModel} to {ToModel}", fromModel, toModel);

        // Validate models have the same name
        if (fromModel.Name != toModel.Name)
        {
            return CkMigrationValidationResult.Invalid(
                $"Cannot migrate between different CK models: {fromModel.Name} and {toModel.Name}");
        }

        // Find migration path
        var migrationPath = await FindMigrationPathAsync(fromModel, toModel, cancellationToken)
            .ConfigureAwait(false);

        if (migrationPath == null)
        {
            return CkMigrationValidationResult.Invalid(
                $"No migration path found from {fromModel} to {toModel}");
        }

        var result = CkMigrationValidationResult.Valid();

        // Validate each step in the path
        foreach (var step in migrationPath.Steps)
        {
            // Get the script - either pre-loaded or load from path
            CkMigrationScriptDto? script = step.Script;

            if (script == null && !string.IsNullOrEmpty(step.ScriptPath))
            {
                if (!File.Exists(step.ScriptPath))
                {
                    result.IsValid = false;
                    result.Errors.Add(new CkMigrationValidationIssue
                    {
                        Message = $"Migration script not found: {step.ScriptPath}"
                    });
                    continue;
                }

                try
                {
                    script = await _migrationParser.ParseScriptAsync(step.ScriptPath!, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.IsValid = false;
                    result.Errors.Add(new CkMigrationValidationIssue
                    {
                        Message = $"Failed to parse migration script: {ex.Message}"
                    });
                    continue;
                }
            }

            if (script == null)
            {
                result.IsValid = false;
                result.Errors.Add(new CkMigrationValidationIssue
                {
                    Message = $"No migration script available for step {step.FromVersion} -> {step.ToVersion}"
                });
                continue;
            }

            // Validate script steps
            foreach (var migrationStep in script.Steps)
            {
                if (string.IsNullOrEmpty(migrationStep.StepId))
                {
                    result.Errors.Add(new CkMigrationValidationIssue
                    {
                        Message = "Step ID is required"
                    });
                }

                if (migrationStep is { Action: CkMigrationActionType.Transform, Transform: null })
                {
                    result.Errors.Add(new CkMigrationValidationIssue
                    {
                        StepId = migrationStep.StepId,
                        Message = "Transform configuration is required for Transform action"
                    });
                }
            }

            // Estimate affected entities
            // TODO: Actually query the tenant to count matching entities
            result.EstimatedEntitiesAffected += script.Steps.Count;
        }

        result.IsValid = result.Errors.Count == 0;

        if (migrationPath.HasBreakingChanges)
        {
            result.Warnings.Add(new CkMigrationValidationIssue
            {
                Message = "This migration contains breaking changes"
            });
        }

        return result;
    }

    private async Task<CkMigrationStepResult> ExecuteMigrationStepAsync(
        string tenantId,
        CkMigrationStep step,
        CkMigrationOptions options,
        CancellationToken cancellationToken)
    {
        var result = new CkMigrationStepResult();

        try
        {
            // Use the pre-loaded script if available, otherwise try loading from path
            var script = step.Script;
            if (script == null && !string.IsNullOrEmpty(step.ScriptPath))
            {
                script = await _migrationParser.ParseScriptAsync(step.ScriptPath!, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (script == null)
            {
                result.Success = false;
                result.Errors.Add($"No migration script available for step {step.FromVersion} -> {step.ToVersion}");
                return result;
            }

            // Execute each step in the script
            foreach (var migrationStep in script.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Executing migration step: {StepId}", migrationStep.StepId);

                var stepResult = await ExecuteScriptStepAsync(tenantId, migrationStep, options, cancellationToken)
                    .ConfigureAwait(false);

                result.EntitiesAdded += stepResult.Added;
                result.EntitiesUpdated += stepResult.Updated;
                result.EntitiesDeleted += stepResult.Deleted;

                if (!stepResult.Success)
                {
                    result.Errors.Add($"Step '{migrationStep.StepId}' failed: {stepResult.Error}");

                    if (!options.ContinueOnError && !migrationStep.ContinueOnError)
                    {
                        result.Success = false;
                        return result;
                    }
                }
            }

            // Run post-validations
            if (script.PostValidations != null)
            {
                foreach (var validation in script.PostValidations)
                {
                    var validationResult = await RunPostValidationAsync(tenantId, validation, cancellationToken)
                        .ConfigureAwait(false);

                    if (!validationResult.Passed)
                    {
                        if (validation.Severity == CkMigrationValidationSeverity.Error)
                        {
                            result.Errors.Add($"Validation '{validation.ValidationId}' failed: {validationResult.Message}");
                        }
                        else
                        {
                            result.Warnings.Add($"Validation '{validation.ValidationId}': {validationResult.Message}");
                        }
                    }
                }
            }

            result.Success = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Failed to execute migration step: {ex.Message}");
        }

        return result;
    }

    private async Task<(bool Success, int Added, int Updated, int Deleted, string? Error)> ExecuteScriptStepAsync(
        string tenantId,
        CkMigrationStepDto step,
        CkMigrationOptions options,
        CancellationToken cancellationToken)
    {
        if (options.DryRun)
        {
            _logger.LogInformation("DryRun: Would execute step {StepId} with action {Action}",
                step.StepId, step.Action);
            return (true, 0, 0, 0, null);
        }

        var repository = await _repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (repository == null)
        {
            return (false, 0, 0, 0, $"No repository available for tenant {tenantId}");
        }

        try
        {
            switch (step.Action)
            {
                case CkMigrationActionType.Transform:
                    return await ExecuteTransformAsync(repository, step, cancellationToken)
                        .ConfigureAwait(false);

                case CkMigrationActionType.Update:
                    return await ExecuteUpdateAsync(repository, step, cancellationToken)
                        .ConfigureAwait(false);

                case CkMigrationActionType.Delete:
                    return await ExecuteDeleteAsync(repository, step, cancellationToken)
                        .ConfigureAwait(false);

                case CkMigrationActionType.Add:
                    return await ExecuteAddAsync(repository, step, cancellationToken)
                        .ConfigureAwait(false);

                default:
                    return (false, 0, 0, 0, $"Unknown action type: {step.Action}");
            }
        }
        catch (Exception ex)
        {
            return (false, 0, 0, 0, ex.Message);
        }
    }

    private async Task<(bool Success, int Added, int Updated, int Deleted, string? Error)> ExecuteTransformAsync(
        IRuntimeRepository repository,
        CkMigrationStepDto step,
        CancellationToken cancellationToken)
    {
        if (step.Target == null || string.IsNullOrEmpty(step.Target.CkTypeId))
        {
            return (false, 0, 0, 0, "Transform step requires a target with CkTypeId");
        }

        if (step.Transform == null)
        {
            return (false, 0, 0, 0, "Transform step requires transform configuration");
        }

        _logger.LogInformation("Transform step {StepId}: Target={CkTypeId}, Transform={TransformType}",
            step.StepId, step.Target.CkTypeId, step.Transform.Type);

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        session.StartTransaction();

        try
        {
            var sourceCkTypeId = ParseCkTypeId(step.Target.CkTypeId);
            var queryOptions = BuildQueryOptions(step.Target);

            var resultSet = await repository.GetRtEntitiesByTypeAsync(session, sourceCkTypeId, queryOptions)
                .ConfigureAwait(false);

            int updatedCount = 0;

            // Check if this is a ChangeCkType transform - requires special handling
            var isChangeCkType = step.Transform.Type == CkMigrationTransformType.ChangeCkType &&
                                 !string.IsNullOrEmpty(step.Transform.NewCkTypeId);

            RtCkId<CkTypeId>? targetCkTypeId = null;
            if (isChangeCkType)
            {
                targetCkTypeId = ParseCkTypeId(step.Transform.NewCkTypeId);
                _logger.LogDebug("ChangeCkType migration: Moving entities from {SourceType} to {TargetType}",
                    sourceCkTypeId, targetCkTypeId);
            }

            foreach (var entity in resultSet.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (isChangeCkType && targetCkTypeId != null)
                {
                    // For ChangeCkType: Delete from old collection and insert into new collection
                    // This is necessary because entities are stored in type-specific collections

                    // 1. Delete from source collection
                    await repository.DeleteOneRtEntityByRtIdAsync(session, sourceCkTypeId, entity.RtId,
                        DeleteOptions.Erase).ConfigureAwait(false);

                    // 2. Change the entity's CkTypeId to the new type
                    entity.CkTypeId = targetCkTypeId;

                    // 3. Apply any other transformations (but not the ckTypeId attribute since we set it on root level)
                    ApplyTransformationExceptCkType(entity, step.Transform);

                    // 4. Insert into target collection
                    await repository.InsertOneRtEntityAsync(session, targetCkTypeId, entity)
                        .ConfigureAwait(false);
                }
                else
                {
                    // For other transforms: simple in-place update
                    ApplyTransformation(entity, step.Transform);

                    await repository.UpdateOneRtEntityByIdAsync(session, sourceCkTypeId, entity.RtId, entity)
                        .ConfigureAwait(false);
                }

                updatedCount++;
            }

            await session.CommitTransactionAsync().ConfigureAwait(false);

            _logger.LogDebug("Transform step completed: {Count} entities updated", updatedCount);
            return (true, 0, updatedCount, 0, null);
        }
        catch (Exception ex)
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
            _logger.LogError(ex, "Error executing transform step {StepId}", step.StepId);
            return (false, 0, 0, 0, ex.Message);
        }
    }

    private async Task<(bool Success, int Added, int Updated, int Deleted, string? Error)> ExecuteUpdateAsync(
        IRuntimeRepository repository,
        CkMigrationStepDto step,
        CancellationToken cancellationToken)
    {
        if (step.Target == null || string.IsNullOrEmpty(step.Target.CkTypeId))
        {
            return (false, 0, 0, 0, "Update step requires a target with CkTypeId");
        }

        _logger.LogInformation("Update step {StepId}: Target={CkTypeId}", step.StepId, step.Target.CkTypeId);

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        session.StartTransaction();

        try
        {
            var ckTypeId = ParseCkTypeId(step.Target.CkTypeId);
            var queryOptions = BuildQueryOptions(step.Target);

            var resultSet = await repository.GetRtEntitiesByTypeAsync(session, ckTypeId, queryOptions)
                .ConfigureAwait(false);

            int updatedCount = 0;

            foreach (var entity in resultSet.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Apply data updates from step.Data
                if (step.Data != null)
                {
                    foreach (var kvp in step.Data)
                    {
                        entity.SetAttributeValue(kvp.Key,
                            AttributeValueTypesDto.String,
                            kvp.Value);
                    }
                }

                await repository.UpdateOneRtEntityByIdAsync(session, ckTypeId, entity.RtId, entity)
                    .ConfigureAwait(false);

                updatedCount++;
            }

            await session.CommitTransactionAsync().ConfigureAwait(false);

            _logger.LogDebug("Update step completed: {Count} entities updated", updatedCount);
            return (true, 0, updatedCount, 0, null);
        }
        catch (Exception ex)
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
            _logger.LogError(ex, "Error executing update step {StepId}", step.StepId);
            return (false, 0, 0, 0, ex.Message);
        }
    }

    private async Task<(bool Success, int Added, int Updated, int Deleted, string? Error)> ExecuteDeleteAsync(
        IRuntimeRepository repository,
        CkMigrationStepDto step,
        CancellationToken cancellationToken)
    {
        if (step.Target == null || string.IsNullOrEmpty(step.Target.CkTypeId))
        {
            return (false, 0, 0, 0, "Delete step requires a target with CkTypeId");
        }

        _logger.LogInformation("Delete step {StepId}: Target={CkTypeId}", step.StepId, step.Target.CkTypeId);

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        session.StartTransaction();

        try
        {
            var ckTypeId = ParseCkTypeId(step.Target.CkTypeId);
            var filterCriteria = BuildFilterCriteria(step.Target);

            await repository.DeleteManyRtEntitiesAsync(session, ckTypeId, filterCriteria,
                DeleteOptions.Erase)
                .ConfigureAwait(false);

            await session.CommitTransactionAsync().ConfigureAwait(false);

            // Note: We don't have an easy way to get the count from DeleteManyRtEntitiesAsync
            // so we return 1 as a placeholder
            _logger.LogDebug("Delete step completed");
            return (true, 0, 0, 1, null);
        }
        catch (Exception ex)
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
            _logger.LogError(ex, "Error executing delete step {StepId}", step.StepId);
            return (false, 0, 0, 0, ex.Message);
        }
    }

    private async Task<(bool Success, int Added, int Updated, int Deleted, string? Error)> ExecuteAddAsync(
        IRuntimeRepository repository,
        CkMigrationStepDto step,
        CancellationToken cancellationToken)
    {
        if (step.Target == null || string.IsNullOrEmpty(step.Target.CkTypeId))
        {
            return (false, 0, 0, 0, "Add step requires a target with CkTypeId");
        }

        _logger.LogInformation("Add step {StepId}: Target={CkTypeId}", step.StepId, step.Target.CkTypeId);

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        session.StartTransaction();

        try
        {
            var ckTypeId = ParseCkTypeId(step.Target.CkTypeId);
            var newEntity = await repository.CreateTransientRtEntityByRtCkIdAsync(ckTypeId)
                .ConfigureAwait(false);

            // Apply data from step.Data
            if (step.Data != null)
            {
                foreach (var kvp in step.Data)
                {
                    newEntity.SetAttributeValue(kvp.Key,
                        AttributeValueTypesDto.String,
                        kvp.Value);
                }
            }

            await repository.InsertOneRtEntityAsync(session, ckTypeId, newEntity)
                .ConfigureAwait(false);

            await session.CommitTransactionAsync().ConfigureAwait(false);

            _logger.LogDebug("Add step completed: 1 entity added");
            return (true, 1, 0, 0, null);
        }
        catch (Exception ex)
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
            _logger.LogError(ex, "Error executing add step {StepId}", step.StepId);
            return (false, 0, 0, 0, ex.Message);
        }
    }

    private static RtCkId<CkTypeId> ParseCkTypeId(string? ckTypeIdString)
    {
        if (string.IsNullOrEmpty(ckTypeIdString))
        {
            throw new ArgumentNullException(nameof(ckTypeIdString), "CkTypeId cannot be null or empty");
        }

        // Expected format: "ModelName/TypeName" or just "TypeName" (defaults to "System")
        var parts = ckTypeIdString!.Split('/');
        if (parts.Length == 2)
        {
            return new RtCkId<CkTypeId>(parts[0], parts[1]);
        }
        return new RtCkId<CkTypeId>("System", ckTypeIdString);
    }

    private static RtEntityQueryOptions BuildQueryOptions(CkMigrationTargetDto target)
    {
        var options = RtEntityQueryOptions.Create();

        if (!string.IsNullOrEmpty(target.RtId))
        {
            options.Field("rtId", FieldFilterOperator.Equals, target.RtId);
        }

        if (!string.IsNullOrEmpty(target.RtWellKnownName))
        {
            options.Field("rtWellKnownName", FieldFilterOperator.Equals, target.RtWellKnownName);
        }

        if (target.Filter != null)
        {
            ApplyFilterToOptions(options, target.Filter);
        }

        return options;
    }

    private static void ApplyFilterToOptions(RtEntityQueryOptions options, CkMigrationFilterDto filter)
    {
        // Apply single filter
        if (!string.IsNullOrEmpty(filter.Attribute) && filter.Operator.HasValue)
        {
            var op = ParseFilterOperator(filter.Operator.Value);
            options.Field(filter.Attribute!, op, filter.Value);
        }

        // Apply AND filters
        if (filter.And != null)
        {
            foreach (var andFilter in filter.And)
            {
                ApplyFilterToOptions(options, andFilter);
            }
        }

        // Note: OR filters would require nested FieldFilterCriteria which is more complex
    }

    private static FieldFilterCriteria BuildFilterCriteria(CkMigrationTargetDto target)
    {
        var criteria = FieldFilterCriteria.Create();

        if (!string.IsNullOrEmpty(target.RtId))
        {
            criteria.Field("rtId", FieldFilterOperator.Equals, target.RtId);
        }

        if (!string.IsNullOrEmpty(target.RtWellKnownName))
        {
            criteria.Field("rtWellKnownName", FieldFilterOperator.Equals, target.RtWellKnownName);
        }

        if (target.Filter != null)
        {
            ApplyFilterToCriteria(criteria, target.Filter);
        }

        return criteria;
    }

    private static void ApplyFilterToCriteria(FieldFilterCriteria criteria, CkMigrationFilterDto filter)
    {
        // Apply single filter
        if (!string.IsNullOrEmpty(filter.Attribute) && filter.Operator.HasValue)
        {
            var op = ParseFilterOperator(filter.Operator.Value);
            criteria.Field(filter.Attribute!, op, filter.Value);
        }

        // Apply AND filters
        if (filter.And != null)
        {
            foreach (var andFilter in filter.And)
            {
                ApplyFilterToCriteria(criteria, andFilter);
            }
        }
    }

    private static FieldFilterOperator ParseFilterOperator(CkMigrationFilterOperator filterOperator)
    {
        return filterOperator switch
        {
            CkMigrationFilterOperator.Eq => FieldFilterOperator.Equals,
            CkMigrationFilterOperator.Ne => FieldFilterOperator.NotEquals,
            CkMigrationFilterOperator.Exists => FieldFilterOperator.IsNotNull,
            CkMigrationFilterOperator.NotExists => FieldFilterOperator.IsNull,
            CkMigrationFilterOperator.Contains => FieldFilterOperator.Contains,
            CkMigrationFilterOperator.StartsWith => FieldFilterOperator.StartsWith,
            _ => FieldFilterOperator.Equals
        };
    }

    private void ApplyTransformation(RtEntity entity, CkMigrationTransformDto transform)
    {
        switch (transform.Type)
        {
            case CkMigrationTransformType.ChangeCkType:
                if (!string.IsNullOrEmpty(transform.NewCkTypeId))
                {
                    entity.SetAttributeValue(AttributeCkTypeId,
                        AttributeValueTypesDto.String,
                        transform.NewCkTypeId);
                }
                break;

            case CkMigrationTransformType.SetValue:
                if (!string.IsNullOrEmpty(transform.TargetAttribute))
                {
                    entity.SetAttributeValue(transform.TargetAttribute!,
                        AttributeValueTypesDto.String,
                        transform.Value);
                }
                break;

            case CkMigrationTransformType.RenameAttribute:
                if (!string.IsNullOrEmpty(transform.SourceAttribute) && !string.IsNullOrEmpty(transform.TargetAttribute))
                {
                    var oldValue = entity.GetAttributeValueOrDefault(transform.SourceAttribute!);
                    if (oldValue != null)
                    {
                        entity.SetAttributeValue(transform.TargetAttribute!,
                            AttributeValueTypesDto.String,
                            oldValue);
                        // Note: We can't easily remove the old attribute without modifying the internal dictionary
                    }
                }
                break;

            case CkMigrationTransformType.DeleteAttribute:
                // Note: RtEntity doesn't expose a method to remove attributes
                // We set it to null as a workaround
                if (!string.IsNullOrEmpty(transform.TargetAttribute))
                {
                    entity.SetAttributeValue(transform.TargetAttribute!,
                        AttributeValueTypesDto.String,
                        null);
                }
                break;

            case CkMigrationTransformType.CopyAttribute:
                if (!string.IsNullOrEmpty(transform.SourceAttribute) && !string.IsNullOrEmpty(transform.TargetAttribute))
                {
                    var valueToCopy = entity.GetAttributeValueOrDefault(transform.SourceAttribute!);
                    entity.SetAttributeValue(transform.TargetAttribute!,
                        AttributeValueTypesDto.String,
                        valueToCopy);
                }
                break;

            case CkMigrationTransformType.MapValue:
                if (!string.IsNullOrEmpty(transform.TargetAttribute) && transform.ValueMapping != null)
                {
                    var currentValue = entity.GetAttributeValueOrDefault(transform.TargetAttribute!)?.ToString();
                    if (currentValue != null && transform.ValueMapping.TryGetValue(currentValue, out var mappedValue))
                    {
                        entity.SetAttributeValue(transform.TargetAttribute!,
                            AttributeValueTypesDto.String,
                            mappedValue);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Applies transformation to an entity, but skips ChangeCkType since the CkTypeId
    /// is already set on the root level when moving entities between collections.
    /// </summary>
    private void ApplyTransformationExceptCkType(RtEntity entity, CkMigrationTransformDto transform)
    {
        // For ChangeCkType, we don't set the attribute because the root-level CkTypeId
        // is already set and the entity will be inserted into the correct collection.
        // This prevents having both root-level and attribute-level ckTypeId values.
        if (transform.Type == CkMigrationTransformType.ChangeCkType)
        {
            return;
        }

        // For all other transform types, use the normal transformation
        ApplyTransformation(entity, transform);
    }

    private Task<(bool Passed, string? Message)> RunPostValidationAsync(
        string tenantId,
        CkMigrationPostValidationDto validation,
        CancellationToken cancellationToken)
    {
        // TODO: Implement actual validation using runtime repository

        _logger.LogDebug("Running post-validation: {ValidationId}", validation.ValidationId);

        return Task.FromResult((true, (string?)null));
    }

    private async Task RecordMigrationHistoryAsync(
        string tenantId,
        CkMigrationResult result,
        long durationMs,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Recording migration history for tenant {TenantId}: {FromModel} -> {ToModel}, Success={Success}",
            tenantId, result.FromModel, result.ToModel, result.Success);

        var repository = await _repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (repository == null)
        {
            _logger.LogWarning("No repository available for tenant {TenantId}, cannot record migration history", tenantId);
            return;
        }

        try
        {
            var session = await repository.GetSessionAsync().ConfigureAwait(false);
            session.StartTransaction();

            try
            {
                var ckTypeId = new RtCkId<CkTypeId>("System", "MigrationHistory");
                var historyEntity = await repository.CreateTransientRtEntityByRtCkIdAsync(ckTypeId)
                    .ConfigureAwait(false);

                historyEntity.SetAttributeValue(AttributeCkModelName,
                    AttributeValueTypesDto.String,
                    result.FromModel.Name);
                historyEntity.SetAttributeValue(AttributeFromVersion,
                    AttributeValueTypesDto.String,
                    result.FromModel.Version.ToString());
                historyEntity.SetAttributeValue(AttributeToVersion,
                    AttributeValueTypesDto.String,
                    result.ToModel.Version.ToString());
                historyEntity.SetAttributeValue(AttributeExecutedAt,
                    AttributeValueTypesDto.DateTime,
                    DateTime.UtcNow);
                historyEntity.SetAttributeValue(AttributeSuccess,
                    AttributeValueTypesDto.Boolean,
                    result.Success);
                historyEntity.SetAttributeValue(AttributeDurationMs,
                    AttributeValueTypesDto.Int64,
                    durationMs);
                historyEntity.SetAttributeValue(AttributeEntitiesAdded,
                    AttributeValueTypesDto.Integer,
                    result.EntitiesAdded);
                historyEntity.SetAttributeValue(AttributeEntitiesUpdated,
                    AttributeValueTypesDto.Integer,
                    result.EntitiesUpdated);
                historyEntity.SetAttributeValue(AttributeEntitiesDeleted,
                    AttributeValueTypesDto.Integer,
                    result.EntitiesDeleted);

                if (result.Errors.Count > 0)
                {
                    historyEntity.SetAttributeValue(AttributeErrors,
                        AttributeValueTypesDto.StringArray,
                        result.Errors.ToList());
                }

                if (!string.IsNullOrEmpty(result.BackupId))
                {
                    historyEntity.SetAttributeValue(AttributeBackupId,
                        AttributeValueTypesDto.String,
                        result.BackupId);
                }

                await repository.InsertOneRtEntityAsync(session, ckTypeId, historyEntity)
                    .ConfigureAwait(false);

                await session.CommitTransactionAsync().ConfigureAwait(false);

                _logger.LogDebug("Migration history recorded successfully for tenant {TenantId}", tenantId);
            }
            catch
            {
                await session.AbortTransactionAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording migration history for tenant {TenantId}", tenantId);
        }
    }

    private async Task<List<CkMigrationStep>?> FindMultiHopPathAsync(
        CkModelId ckModelId,
        List<CkMigrationReferenceDto> migrations,
        string fromVersion,
        string toVersion,
        CancellationToken cancellationToken)
    {
        // Simple BFS to find path (versions only first)
        var queue = new Queue<(string Version, List<CkMigrationReferenceDto> MigrationRefs)>();
        var visited = new HashSet<string>();

        queue.Enqueue((fromVersion, []));
        visited.Add(fromVersion);

        List<CkMigrationReferenceDto>? foundPath = null;

        while (queue.Count > 0)
        {
            var (currentVersion, currentPath) = queue.Dequeue();

            if (currentVersion == toVersion)
            {
                foundPath = currentPath;
                break;
            }

            foreach (var migration in migrations.Where(m => m.FromVersion == currentVersion))
            {
                if (!visited.Add(migration.ToVersion))
                {
                    continue;
                }

                var newPath = new List<CkMigrationReferenceDto>(currentPath) { migration };

                if (migration.ToVersion == toVersion)
                {
                    foundPath = newPath;
                    break;
                }

                queue.Enqueue((migration.ToVersion, newPath));
            }

            if (foundPath != null)
            {
                break;
            }
        }

        if (foundPath == null || foundPath.Count == 0)
        {
            return null;
        }

        // Load scripts for the found path
        var steps = new List<CkMigrationStep>();
        foreach (var migrationRef in foundPath)
        {
            var script = await _contentProvider.GetMigrationAsync(
                ckModelId, migrationRef.FromVersion, migrationRef.ToVersion, cancellationToken)
                .ConfigureAwait(false);

            steps.Add(new CkMigrationStep
            {
                FromVersion = migrationRef.FromVersion,
                ToVersion = migrationRef.ToVersion,
                Script = script,
                Description = migrationRef.Description,
                Breaking = migrationRef.Breaking
            });
        }

        return steps;
    }

    private async Task<CkModelId?> GetLatestCkModelVersionAsync(string ckModelName, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting latest version for CK model {CkModelName}", ckModelName);

        try
        {
            var listResult = await _catalogService.ListAsync(0, 1000, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var latestModel = listResult.ModelResultItems
                .Where(m => m.ModelId.Name.Equals(ckModelName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.ModelId.Version)
                .FirstOrDefault();

            if (latestModel != null)
            {
                _logger.LogDebug("Latest version for {CkModelName}: {Version}", ckModelName, latestModel.ModelId);
                return latestModel.ModelId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting latest version for CK model {CkModelName}", ckModelName);
        }

        return null;
    }

    private class CkMigrationStepResult
    {
        public bool Success { get; set; } = true;
        public int EntitiesAdded { get; set; }
        public int EntitiesUpdated { get; set; }
        public int EntitiesDeleted { get; set; }
        public List<string> Errors { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }
}
