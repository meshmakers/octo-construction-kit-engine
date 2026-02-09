using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// File system-based implementation of <see cref="ICkMigrationContentProvider"/>.
/// Loads migration scripts from local file system paths.
/// </summary>
public class FileSystemCkMigrationContentProvider : ICkMigrationContentProvider
{
    private readonly ICkMigrationParser _parser;
    private readonly ILogger<FileSystemCkMigrationContentProvider> _logger;

    /// <summary>
    /// Dictionary mapping CK model names to their source paths
    /// </summary>
    private readonly Dictionary<string, string> _modelSourcePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new instance of <see cref="FileSystemCkMigrationContentProvider"/>
    /// </summary>
    public FileSystemCkMigrationContentProvider(
        ICkMigrationParser parser,
        ILogger<FileSystemCkMigrationContentProvider> logger)
    {
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Registers a source path for a CK model's migrations
    /// </summary>
    /// <param name="ckModelName">Name of the CK model</param>
    /// <param name="sourcePath">Path to the CK model source directory containing ConstructionKit/migrations</param>
    public void RegisterModelSourcePath(string ckModelName, string sourcePath)
    {
        _modelSourcePaths[ckModelName] = sourcePath;
        _logger.LogDebug("Registered migration source path for CK model {CkModelName}: {Path}", ckModelName, sourcePath);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CkMigrationScriptDto>> GetMigrationsAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default)
    {
        var migrations = new List<CkMigrationScriptDto>();

        var meta = await GetMigrationMetaAsync(ckModelId, cancellationToken).ConfigureAwait(false);
        if (meta == null)
        {
            _logger.LogDebug("No migration metadata found for CK model {CkModelId}", ckModelId);
            return migrations;
        }

        var migrationsPath = GetMigrationsPath(ckModelId);
        if (migrationsPath == null)
        {
            return migrations;
        }

        foreach (var migrationRef in meta.Migrations)
        {
            try
            {
                var scriptPath = Path.Combine(migrationsPath, migrationRef.ScriptPath);
                if (File.Exists(scriptPath))
                {
                    var script = await _parser.ParseScriptAsync(scriptPath, cancellationToken).ConfigureAwait(false);
                    migrations.Add(script);
                    _logger.LogDebug("Loaded migration script {FromVersion} -> {ToVersion} for {CkModelId}",
                        migrationRef.FromVersion, migrationRef.ToVersion, ckModelId);
                }
                else
                {
                    _logger.LogWarning("Migration script file not found: {ScriptPath}", scriptPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse migration script {ScriptPath} for {CkModelId}",
                    migrationRef.ScriptPath, ckModelId);
            }
        }

        return migrations.OrderBy(m => m.SourceVersion).ToList();
    }

    /// <inheritdoc />
    public async Task<CkMigrationMetaDto?> GetMigrationMetaAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default)
    {
        var migrationsPath = GetMigrationsPath(ckModelId);
        if (migrationsPath == null)
        {
            return null;
        }

        var metaPath = Path.Combine(migrationsPath, "migration-meta.yaml");
        if (!File.Exists(metaPath))
        {
            _logger.LogDebug("Migration meta file not found at {MetaPath}", metaPath);
            return null;
        }

        try
        {
            return await _parser.ParseMetaAsync(metaPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse migration meta at {MetaPath}", metaPath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<CkMigrationScriptDto?> GetMigrationAsync(
        CkModelId ckModelId,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        var meta = await GetMigrationMetaAsync(ckModelId, cancellationToken).ConfigureAwait(false);
        if (meta == null)
        {
            return null;
        }

        var migrationRef = meta.Migrations.FirstOrDefault(m =>
            m.FromVersion == sourceVersion && m.ToVersion == targetVersion);

        if (migrationRef == null)
        {
            _logger.LogDebug("No migration found from {SourceVersion} to {TargetVersion} for {CkModelId}",
                sourceVersion, targetVersion, ckModelId);
            return null;
        }

        var migrationsPath = GetMigrationsPath(ckModelId);
        if (migrationsPath == null)
        {
            return null;
        }

        var scriptPath = Path.Combine(migrationsPath, migrationRef.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            _logger.LogWarning("Migration script file not found: {ScriptPath}", scriptPath);
            return null;
        }

        try
        {
            return await _parser.ParseScriptAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse migration script at {ScriptPath}", scriptPath);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> HasMigrationsAsync(CkModelId ckModelId, CancellationToken cancellationToken = default)
    {
        var migrationsPath = GetMigrationsPath(ckModelId);
        if (migrationsPath == null)
        {
            return Task.FromResult(false);
        }

        var metaPath = Path.Combine(migrationsPath, "migration-meta.yaml");
        return Task.FromResult(File.Exists(metaPath));
    }

    private string? GetMigrationsPath(CkModelId ckModelId)
    {
        if (!_modelSourcePaths.TryGetValue(ckModelId.Name, out var sourcePath))
        {
            _logger.LogDebug("No source path registered for CK model {CkModelName}", ckModelId.Name);
            return null;
        }

        // Try standard path structure: ConstructionKit/migrations
        var migrationsPath = Path.Combine(sourcePath, "ConstructionKit", "migrations");
        if (Directory.Exists(migrationsPath))
        {
            return migrationsPath;
        }

        // Try alternative path structure: migrations
        migrationsPath = Path.Combine(sourcePath, "migrations");
        if (Directory.Exists(migrationsPath))
        {
            return migrationsPath;
        }

        _logger.LogDebug("No migrations folder found for CK model {CkModelId} at {SourcePath}", ckModelId, sourcePath);
        return null;
    }
}
