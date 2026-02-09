using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// In-memory implementation of <see cref="ICkMigrationContentProvider"/> that serves
/// migration data from compiled CK model data (inline migrations).
/// This provider is populated during CK model import when the compiled model
/// contains embedded migration data.
/// </summary>
public class CompiledModelCkMigrationContentProvider : ICkMigrationContentProvider
{
    private readonly ConcurrentDictionary<string, CkCompiledMigrationDataDto> _migrationData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CompiledModelCkMigrationContentProvider> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="CompiledModelCkMigrationContentProvider"/>
    /// </summary>
    public CompiledModelCkMigrationContentProvider(ILogger<CompiledModelCkMigrationContentProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Stores migration data for a CK model, making it available for migration execution.
    /// </summary>
    /// <param name="ckModelId">The CK model ID</param>
    /// <param name="migrationData">The compiled migration data</param>
    public void SetMigrationData(CkModelId ckModelId, CkCompiledMigrationDataDto migrationData)
    {
        _migrationData[ckModelId.Name] = migrationData;
        _logger.LogDebug("Stored compiled migration data for CK model {CkModelName} with {ScriptCount} scripts",
            ckModelId.Name, migrationData.Scripts.Count);
    }

    /// <summary>
    /// Removes migration data for a specific CK model.
    /// </summary>
    /// <param name="ckModelId">The CK model ID to clear data for</param>
    public void ClearMigrationData(CkModelId ckModelId)
    {
        if (_migrationData.TryRemove(ckModelId.Name, out _))
        {
            _logger.LogDebug("Cleared compiled migration data for CK model {CkModelName}", ckModelId.Name);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CkMigrationScriptDto>> GetMigrationsAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default)
    {
        if (!_migrationData.TryGetValue(ckModelId.Name, out var data))
        {
            return Task.FromResult<IReadOnlyList<CkMigrationScriptDto>>([]);
        }

        IReadOnlyList<CkMigrationScriptDto> scripts = data.Scripts
            .OrderBy(s => s.SourceVersion)
            .ToList();

        _logger.LogDebug("Returning {Count} compiled migration scripts for {CkModelId}",
            scripts.Count, ckModelId);

        return Task.FromResult(scripts);
    }

    /// <inheritdoc />
    public Task<CkMigrationMetaDto?> GetMigrationMetaAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default)
    {
        if (!_migrationData.TryGetValue(ckModelId.Name, out var data))
        {
            return Task.FromResult<CkMigrationMetaDto?>(null);
        }

        return Task.FromResult<CkMigrationMetaDto?>(data.Meta);
    }

    /// <inheritdoc />
    public Task<CkMigrationScriptDto?> GetMigrationAsync(
        CkModelId ckModelId,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken = default)
    {
        if (!_migrationData.TryGetValue(ckModelId.Name, out var data))
        {
            return Task.FromResult<CkMigrationScriptDto?>(null);
        }

        var script = data.Scripts.FirstOrDefault(s =>
            s.SourceVersion == sourceVersion && s.TargetVersion == targetVersion);

        if (script == null)
        {
            _logger.LogDebug("No compiled migration found from {SourceVersion} to {TargetVersion} for {CkModelId}",
                sourceVersion, targetVersion, ckModelId);
        }

        return Task.FromResult(script);
    }

    /// <inheritdoc />
    public Task<bool> HasMigrationsAsync(CkModelId ckModelId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_migrationData.ContainsKey(ckModelId.Name));
    }
}
