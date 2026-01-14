using System.Reflection;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Embedded resource-based implementation of <see cref="ICkMigrationContentProvider"/>.
/// Loads migration scripts from embedded resources within assemblies.
/// </summary>
/// <remarks>
/// This provider expects migration files to be embedded with the following naming convention:
/// - Migration meta: {Namespace}.migrations.migration-meta.yaml
/// - Migration scripts: {Namespace}.migrations.{script-filename}.yaml
///
/// Example: MyApp.CkModel.migrations.migration-meta.yaml
///          MyApp.CkModel.migrations.1-0-0-to-2-0-0.yaml
/// </remarks>
public class EmbeddedCkMigrationContentProvider : ICkMigrationContentProvider
{
    private readonly ICkMigrationParser _parser;
    private readonly ILogger<EmbeddedCkMigrationContentProvider> _logger;

    /// <summary>
    /// Registered sources mapping CK model names to assembly/namespace combinations
    /// </summary>
    private readonly Dictionary<string, EmbeddedMigrationSource> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new instance of <see cref="EmbeddedCkMigrationContentProvider"/>
    /// </summary>
    public EmbeddedCkMigrationContentProvider(
        ICkMigrationParser parser,
        ILogger<EmbeddedCkMigrationContentProvider> logger,
        IEnumerable<ICkEmbeddedMigrationSource> migrationSources)
    {
        _parser = parser;
        _logger = logger;

        // Auto-register all migration sources from DI
        foreach (var source in migrationSources)
        {
            RegisterMigrationSource(source.CkModelName, source.Assembly, source.ResourceNamespace);
        }
    }

    /// <summary>
    /// Registers an assembly as a source for migrations for a specific CK model.
    /// </summary>
    /// <param name="ckModelName">Name of the CK model</param>
    /// <param name="assembly">Assembly containing the embedded migration resources</param>
    /// <param name="resourceNamespace">Base namespace for the migration resources</param>
    public void RegisterMigrationSource(string ckModelName, Assembly assembly, string resourceNamespace)
    {
        _sources[ckModelName] = new EmbeddedMigrationSource(assembly, resourceNamespace);
        _logger.LogDebug("Registered embedded migration source for CK model {CkModelName}: Assembly={Assembly}, Namespace={Namespace}",
            ckModelName, assembly.GetName().Name, resourceNamespace);
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

        if (!_sources.TryGetValue(ckModelId.Name, out var source))
        {
            return migrations;
        }

        foreach (var migrationRef in meta.Migrations)
        {
            try
            {
                var resourceName = GetScriptResourceName(source, migrationRef.ScriptPath);
                using var stream = source.Assembly.GetManifestResourceStream(resourceName);

                if (stream != null)
                {
                    var script = await _parser.ParseScriptFromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
                    migrations.Add(script);
                    _logger.LogDebug("Loaded embedded migration script {FromVersion} -> {ToVersion} for {CkModelId}",
                        migrationRef.FromVersion, migrationRef.ToVersion, ckModelId);
                }
                else
                {
                    _logger.LogWarning("Embedded migration script not found: {ResourceName}", resourceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse embedded migration script {ScriptPath} for {CkModelId}",
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
        if (!_sources.TryGetValue(ckModelId.Name, out var source))
        {
            _logger.LogDebug("No embedded migration source registered for CK model {CkModelName}", ckModelId.Name);
            return null;
        }

        var resourceName = GetMetaResourceName(source);
        using var stream = source.Assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            _logger.LogDebug("Migration meta resource not found: {ResourceName}", resourceName);
            return null;
        }

        try
        {
            return await _parser.ParseMetaFromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse embedded migration meta: {ResourceName}", resourceName);
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

        if (!_sources.TryGetValue(ckModelId.Name, out var source))
        {
            return null;
        }

        var resourceName = GetScriptResourceName(source, migrationRef.ScriptPath);
        using var stream = source.Assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            _logger.LogWarning("Embedded migration script not found: {ResourceName}", resourceName);
            return null;
        }

        try
        {
            return await _parser.ParseScriptFromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse embedded migration script: {ResourceName}", resourceName);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> HasMigrationsAsync(CkModelId ckModelId, CancellationToken cancellationToken = default)
    {
        if (!_sources.TryGetValue(ckModelId.Name, out var source))
        {
            return Task.FromResult(false);
        }

        var resourceName = GetMetaResourceName(source);
        var resourceNames = source.Assembly.GetManifestResourceNames();

        return Task.FromResult(resourceNames.Contains(resourceName));
    }

    private static string GetMetaResourceName(EmbeddedMigrationSource source)
    {
        return $"{source.ResourceNamespace}.migrations.migration-meta.yaml";
    }

    private static string GetScriptResourceName(EmbeddedMigrationSource source, string scriptPath)
    {
        // Convert file path to resource name:
        // - Replace directory separators with dots
        // - Keep the filename as-is
        var normalizedPath = scriptPath
            .Replace('/', '.')
            .Replace('\\', '.');

        return $"{source.ResourceNamespace}.migrations.{normalizedPath}";
    }

    private sealed record EmbeddedMigrationSource(Assembly Assembly, string ResourceNamespace);
}
