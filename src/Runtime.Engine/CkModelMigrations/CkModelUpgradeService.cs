using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.CkModelMigrations;

/// <summary>
/// Service for automatically checking and executing CK model migrations when a new version is loaded
/// </summary>
internal class CkModelUpgradeService : ICkModelUpgradeService
{
    private readonly ICkModelMigrationService _migrationService;
    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ILogger<CkModelUpgradeService> _logger;

    /// <summary>
    /// Well-known attribute names for CK model migration tracking (PascalCase to match CK model)
    /// </summary>
    private const string AttributeCkModelName = "CkModelName";
    private const string AttributeFromVersion = "FromVersion";
    private const string AttributeToVersion = "ToVersion";
    private const string AttributeExecutedAt = "ExecutedAt";
    private const string AttributeSuccess = "Success";
    private static readonly RtCkId<CkTypeId> MigrationHistoryRtCkTypeId = new("System", "MigrationHistory");


    /// <summary>
    /// Creates a new instance of <see cref="CkModelUpgradeService"/>
    /// </summary>
    public CkModelUpgradeService(
        ICkModelMigrationService migrationService,
        IRuntimeRepositoryProvider repositoryProvider,
        ILogger<CkModelUpgradeService> logger)
    {
        _migrationService = migrationService;
        _repositoryProvider = repositoryProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CkModelUpgradeResult> UpgradeModelsAsync(
        string tenantId,
        IEnumerable<CkModelIdVersionRange> newModelIds,
        CkMigrationOptions? options = null,
        IReadOnlyDictionary<string, string>? previouslyInstalledVersions = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CkMigrationOptions();

        _logger.LogInformation("Checking CK model upgrades for tenant {TenantId}", tenantId);

        var result = new CkModelUpgradeResult { Success = true };

        // Group models by name (in case multiple versions of the same model are provided)
        var modelsByName = newModelIds
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.First());

        // Get currently installed versions from MigrationHistory
        var migrationHistoryVersions = await GetInstalledVersionsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // Keep track of original MigrationHistory entries to know which need to be recorded
        var hasHistoryEntry = new HashSet<string>(migrationHistoryVersions.Keys);

        // Merge with previously installed versions (from schema before import)
        // The schema version represents the ACTUAL installed version - it takes precedence over MigrationHistory
        // This handles cases where:
        // 1. MigrationHistory doesn't exist yet for existing tenants
        // 2. The schema was manually modified (e.g., downgraded for testing)
        var installedVersions = migrationHistoryVersions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        if (previouslyInstalledVersions != null)
        {
            foreach (var kvp in previouslyInstalledVersions)
            {
                if (!installedVersions.ContainsKey(kvp.Key))
                {
                    // No MigrationHistory entry - use schema version
                    _logger.LogDebug(
                        "Using schema version {Version} for CK model {CkModelName} (no MigrationHistory entry found)",
                        kvp.Value, kvp.Key);
                    installedVersions[kvp.Key] = kvp.Value;
                }
                else if (installedVersions[kvp.Key] != kvp.Value)
                {
                    // Schema version differs from MigrationHistory - schema is the source of truth
                    // This can happen when the schema was manually modified (e.g., downgraded)
                    _logger.LogWarning(
                        "Schema version {SchemaVersion} differs from MigrationHistory version {HistoryVersion} for CK model {CkModelName}. " +
                        "Using schema version as it reflects the actual installed state.",
                        kvp.Value, installedVersions[kvp.Key], kvp.Key);
                    installedVersions[kvp.Key] = kvp.Value;
                    // Remove from hasHistoryEntry so we'll record the new version
                    hasHistoryEntry.Remove(kvp.Key);
                }
            }
        }

        foreach (var kvp in modelsByName)
        {
            var modelName = kvp.Key;
            var targetModelRange = kvp.Value;

            cancellationToken.ThrowIfCancellationRequested();

            // For version ranges, extract the actual minimum version
            // For exact versions [1.0.0], MinVersion and MaxVersion are equal
            // For ranges, we use the minimum version as the target
            var targetVersion = targetModelRange.ModelVersionRange.MinVersion
                ?? throw new InvalidOperationException(
                    $"Version range {targetModelRange.ModelVersionRange} has no minimum version");
            var targetVersionString = targetVersion.ToString();

            var upgradeInfo = new CkModelUpgradeInfo
            {
                CkModelName = modelName,
                TargetVersion = targetVersionString
            };

            try
            {
                // Check if migration is needed
                if (installedVersions.TryGetValue(modelName, out var installedVersion))
                {
                    upgradeInfo.InstalledVersion = installedVersion;

                    if (installedVersion == targetVersionString)
                    {
                        // Already at target version
                        _logger.LogDebug("CK model {CkModelName} already at version {Version} for tenant {TenantId}",
                            modelName, installedVersion, tenantId);
                        upgradeInfo.UpgradeNeeded = false;

                        // If version came from schema (not MigrationHistory), record it now
                        if (!hasHistoryEntry.Contains(modelName))
                        {
                            _logger.LogInformation(
                                "Recording initial MigrationHistory entry for CK model {CkModelName} version {Version} for tenant {TenantId}",
                                modelName, installedVersion, tenantId);
                            var modelId = new CkModelId(modelName, installedVersion);
                            await RecordInstalledVersionAsync(tenantId, modelId, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        result.SkippedModels.Add(upgradeInfo);
                        continue;
                    }

                    // Check if we need to migrate
                    var fromModel = new CkModelId(modelName, installedVersion);
                    var targetModel = new CkModelId(modelName, targetVersionString);

                    var migrationPath = await _migrationService.FindMigrationPathAsync(
                            fromModel, targetModel, cancellationToken)
                        .ConfigureAwait(false);

                    upgradeInfo.UpgradeNeeded = true;
                    upgradeInfo.MigrationPathAvailable = migrationPath != null;
                    upgradeInfo.HasBreakingChanges = migrationPath?.HasBreakingChanges ?? false;

                    if (migrationPath == null)
                    {
                        _logger.LogWarning(
                            "No migration path from {FromVersion} to {ToVersion} for CK model {CkModelName}",
                            installedVersion, targetVersionString, modelName);

                        // Record the new version anyway (schema-only upgrade)
                        var installedModelId = new CkModelId(modelName, targetVersionString);
                        await RecordInstalledVersionAsync(tenantId, installedModelId, cancellationToken)
                            .ConfigureAwait(false);

                        upgradeInfo.ErrorMessage =
                            $"No migration path available from {installedVersion} to {targetVersionString}. Version recorded without data migration.";
                        result.Warnings.Add(upgradeInfo.ErrorMessage);
                        result.SkippedModels.Add(upgradeInfo);
                        continue;
                    }

                    // Execute migration
                    _logger.LogInformation(
                        "Executing CK model migration for {CkModelName}: {FromVersion} -> {ToVersion}",
                        modelName, installedVersion, targetVersionString);

                    var migrationResult = await _migrationService.MigrateAsync(
                            tenantId, fromModel, targetModel, options, cancellationToken)
                        .ConfigureAwait(false);

                    upgradeInfo.MigrationResult = migrationResult;

                    if (migrationResult.Success)
                    {
                        result.TotalEntitiesAffected += migrationResult.TotalEntitiesAffected;
                        result.UpgradedModels.Add(upgradeInfo);
                        _logger.LogInformation(
                            "CK model migration completed for {CkModelName}: {EntitiesAffected} entities affected",
                            modelName, migrationResult.TotalEntitiesAffected);

                        // Note: MigrationHistory entry is already recorded by CkModelMigrationService.MigrateAsync
                        // with the correct fromVersion and toVersion from the migration result.
                    }
                    else
                    {
                        upgradeInfo.ErrorMessage = string.Join("; ", migrationResult.Errors);
                        result.FailedModels.Add(upgradeInfo);
                        result.Errors.AddRange(migrationResult.Errors);
                        result.Success = false;

                        _logger.LogError("CK model migration failed for {CkModelName}: {Errors}",
                            modelName, string.Join(", ", migrationResult.Errors));

                        if (!options.ContinueOnError)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // Model not found in MigrationHistory
                    // Check if it's installed in schema (previouslyInstalledVersions)
                    if (previouslyInstalledVersions != null &&
                        !previouslyInstalledVersions.ContainsKey(modelName))
                    {
                        // Model not in schema either - not installed in this tenant
                        _logger.LogDebug(
                            "CK model {CkModelName} not installed in tenant {TenantId}, skipping",
                            modelName, tenantId);
                        continue;
                    }

                    // Either no schema info available OR model is in schema but not yet in MigrationHistory
                    // Record as first installation
                    _logger.LogInformation(
                        "Recording initial version for CK model {CkModelName} version {Version} for tenant {TenantId}",
                        modelName, targetVersionString, tenantId);

                    upgradeInfo.UpgradeNeeded = false;
                    var newModelId = new CkModelId(modelName, targetVersionString);
                    await RecordInstalledVersionAsync(tenantId, newModelId, cancellationToken)
                        .ConfigureAwait(false);

                    result.SkippedModels.Add(upgradeInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/executing upgrade for CK model {CkModelName}", modelName);
                upgradeInfo.ErrorMessage = ex.Message;
                result.FailedModels.Add(upgradeInfo);
                result.Errors.Add($"Error upgrading {modelName}: {ex.Message}");
                result.Success = false;

                if (!options.ContinueOnError)
                {
                    break;
                }
            }
        }

        _logger.LogInformation(
            "CK model upgrade check completed for tenant {TenantId}: {Upgraded} upgraded, {Skipped} skipped, {Failed} failed",
            tenantId, result.UpgradedModels.Count, result.SkippedModels.Count, result.FailedModels.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<CkModelUpgradeInfo> CheckUpgradeNeededAsync(
        string tenantId,
        string ckModelName,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        var upgradeInfo = new CkModelUpgradeInfo
        {
            CkModelName = ckModelName,
            TargetVersion = targetVersion
        };

        try
        {
            var installedVersions = await GetInstalledVersionsAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);

            if (installedVersions.TryGetValue(ckModelName, out var installedVersion))
            {
                upgradeInfo.InstalledVersion = installedVersion;

                if (installedVersion == targetVersion)
                {
                    upgradeInfo.UpgradeNeeded = false;
                    return upgradeInfo;
                }

                upgradeInfo.UpgradeNeeded = true;

                // Check if migration path exists
                var fromModel = new CkModelId(ckModelName, installedVersion);
                var toModel = new CkModelId(ckModelName, targetVersion);

                var migrationPath = await _migrationService.FindMigrationPathAsync(
                        fromModel, toModel, cancellationToken)
                    .ConfigureAwait(false);

                upgradeInfo.MigrationPathAvailable = migrationPath != null;
                upgradeInfo.HasBreakingChanges = migrationPath?.HasBreakingChanges ?? false;
            }
            else
            {
                // Not installed yet, no upgrade needed
                upgradeInfo.UpgradeNeeded = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking upgrade needed for {CkModelName}", ckModelName);
            upgradeInfo.ErrorMessage = ex.Message;
        }

        return upgradeInfo;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetInstalledVersionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var versions = new Dictionary<string, string>();

        var repository = await _repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (repository == null)
        {
            _logger.LogDebug("No repository available for tenant {TenantId}", tenantId);
            return versions;
        }

        try
        {
            var session = await repository.GetSessionAsync().ConfigureAwait(false);

            var queryOptions = RtEntityQueryOptions.Create();

            var resultSet = await repository.GetRtEntitiesByTypeAsync(session, MigrationHistoryRtCkTypeId, queryOptions)
                .ConfigureAwait(false);

            // Collect all history entries with their execution dates
            var historyEntries = new List<(string ModelName, string ToVersion, DateTime ExecutedAt)>();

            foreach (var entity in resultSet.Items)
            {
                var success = entity.GetAttributeValueOrDefault<bool>(AttributeSuccess);
                if (success != true)
                {
                    continue;
                }

                var modelName = entity.GetAttributeStringValueOrDefault(AttributeCkModelName);
                var toVersion = entity.GetAttributeStringValueOrDefault(AttributeToVersion);
                var executedAt = entity.GetAttributeValueOrDefault<DateTime>(AttributeExecutedAt) ?? DateTime.MinValue;

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (!string.IsNullOrEmpty(modelName) && !string.IsNullOrEmpty(toVersion))
                {
                    // ReSharper disable once RedundantSuppressNullableWarningExpression
                    historyEntries.Add((modelName!,
                        // ReSharper disable once RedundantSuppressNullableWarningExpression
                        toVersion!, executedAt));
                }
            }

            // Get the latest successful migration for each model
            foreach (var group in historyEntries.GroupBy(e => e.ModelName))
            {
                var latest = group.OrderByDescending(e => e.ExecutedAt).First();
                // Normalize version string: remove brackets if present (legacy data cleanup)
                // Old code stored "[2.0.1]" instead of "2.0.1"
                versions[latest.ModelName] = NormalizeVersionString(latest.ToVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting installed versions for tenant {TenantId}", tenantId);
        }

        return versions;
    }

    /// <inheritdoc />
    public async Task RecordInstalledVersionAsync(
        string tenantId,
        CkModelId modelId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Recording installed version {CkModelId} for tenant {TenantId}", modelId, tenantId);

        var repository = await _repositoryProvider.GetRepositoryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (repository == null)
        {
            _logger.LogWarning("No repository available for tenant {TenantId}, cannot record installed version",
                tenantId);
            return;
        }

        // Use the exact version from the CkModelId
        var versionString = modelId.Version.ToString();

        try
        {
            var session = await repository.GetSessionAsync().ConfigureAwait(false);
            session.StartTransaction();

            try
            {
                var historyEntity = await repository.CreateTransientRtEntityByRtCkIdAsync(MigrationHistoryRtCkTypeId)
                    .ConfigureAwait(false);

                historyEntity.SetAttributeValue(AttributeCkModelName,
                    AttributeValueTypesDto.String,
                    modelId.Name);
                historyEntity.SetAttributeValue(AttributeFromVersion,
                    AttributeValueTypesDto.String,
                    versionString); // Same as ToVersion for initial install
                historyEntity.SetAttributeValue(AttributeToVersion,
                    AttributeValueTypesDto.String,
                    versionString);
                historyEntity.SetAttributeValue(AttributeExecutedAt,
                    AttributeValueTypesDto.DateTime,
                    DateTime.UtcNow);
                historyEntity.SetAttributeValue(AttributeSuccess,
                    AttributeValueTypesDto.Boolean,
                    true);

                await repository.InsertOneRtEntityAsync(session, MigrationHistoryRtCkTypeId, historyEntity)
                    .ConfigureAwait(false);

                await session.CommitTransactionAsync().ConfigureAwait(false);

                _logger.LogDebug("Recorded installed version {CkModelId} for tenant {TenantId}", modelId, tenantId);
            }
            catch
            {
                await session.AbortTransactionAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording installed version for tenant {TenantId}", tenantId);
        }
    }

    /// <summary>
    /// Normalizes a version string by removing version range brackets if present.
    /// This handles legacy data where versions were stored as "[2.0.1]" instead of "2.0.1".
    /// </summary>
    /// <param name="version">The version string to normalize</param>
    /// <returns>The normalized version string without brackets</returns>
    private static string NormalizeVersionString(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return version;
        }

        // Handle exact version range format: [1.0.0]
        if (version.StartsWith("[") && version.EndsWith("]") && !version.Contains(','))
        {
            return version.Substring(1, version.Length - 2).Trim();
        }

        return version;
    }
}