using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// Aggregates multiple <see cref="ICkMigrationContentProvider"/> instances,
/// allowing migrations to be loaded from multiple sources (embedded resources, file system, etc.)
/// </summary>
/// <remarks>
/// The provider iterates through registered providers in order and returns the first match found.
/// Embedded resources are typically checked first, followed by file system sources.
/// </remarks>
public class AggregateCkMigrationContentProvider : ICkMigrationContentProvider
{
    private readonly List<ICkMigrationContentProvider> _providers = [];
    private readonly ILogger<AggregateCkMigrationContentProvider> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="AggregateCkMigrationContentProvider"/>
    /// </summary>
    public AggregateCkMigrationContentProvider(ILogger<AggregateCkMigrationContentProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a new instance with the given providers
    /// </summary>
    public AggregateCkMigrationContentProvider(
        IEnumerable<ICkMigrationContentProvider> providers,
        ILogger<AggregateCkMigrationContentProvider> logger)
    {
        _providers.AddRange(providers);
        _logger = logger;
    }

    /// <summary>
    /// Adds a provider to the aggregate
    /// </summary>
    /// <param name="provider">Provider to add</param>
    public void AddProvider(ICkMigrationContentProvider provider)
    {
        _providers.Add(provider);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CkMigrationScriptDto>> GetMigrationsAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            if (await provider.HasMigrationsAsync(ckModelId, cancellationToken).ConfigureAwait(false))
            {
                var migrations = await provider.GetMigrationsAsync(ckModelId, cancellationToken).ConfigureAwait(false);
                if (migrations.Count > 0)
                {
                    _logger.LogDebug("Found {Count} migrations for {CkModelId} from {Provider}",
                        migrations.Count, ckModelId, provider.GetType().Name);
                    return migrations;
                }
            }
        }

        _logger.LogDebug("No migrations found for {CkModelId} in any provider", ckModelId);
        return [];
    }

    /// <inheritdoc />
    public async Task<CkMigrationMetaDto?> GetMigrationMetaAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            var meta = await provider.GetMigrationMetaAsync(ckModelId, cancellationToken).ConfigureAwait(false);
            if (meta != null)
            {
                _logger.LogDebug("Found migration meta for {CkModelId} from {Provider}",
                    ckModelId, provider.GetType().Name);
                return meta;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<CkMigrationScriptDto?> GetMigrationAsync(
        CkModelId ckModelId,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            var migration = await provider.GetMigrationAsync(ckModelId, sourceVersion, targetVersion, cancellationToken)
                .ConfigureAwait(false);
            if (migration != null)
            {
                _logger.LogDebug("Found migration {SourceVersion} -> {TargetVersion} for {CkModelId} from {Provider}",
                    sourceVersion, targetVersion, ckModelId, provider.GetType().Name);
                return migration;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> HasMigrationsAsync(CkModelId ckModelId, CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            if (await provider.HasMigrationsAsync(ckModelId, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}
