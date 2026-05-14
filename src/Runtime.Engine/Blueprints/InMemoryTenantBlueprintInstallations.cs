using System.Collections.Concurrent;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// In-memory implementation of <see cref="ITenantBlueprintInstallations"/>.
/// Suitable for tests and hosts that wire a persistent backend via DI override.
/// </summary>
internal class InMemoryTenantBlueprintInstallations : ITenantBlueprintInstallations
{
    // Key: tenantId. Value: blueprintName -> installation.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BlueprintInstallation>> _installations
        = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<BlueprintInstallation>> GetInstalledAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_installations.TryGetValue(tenantId, out var byName))
        {
            return Task.FromResult<IReadOnlyList<BlueprintInstallation>>(
                byName.Values.ToList());
        }

        return Task.FromResult<IReadOnlyList<BlueprintInstallation>>([]);
    }

    /// <inheritdoc />
    public Task<BlueprintInstallation?> GetByBlueprintNameAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default)
    {
        if (_installations.TryGetValue(tenantId, out var byName)
            && byName.TryGetValue(blueprintName, out var installation))
        {
            return Task.FromResult<BlueprintInstallation?>(installation);
        }

        return Task.FromResult<BlueprintInstallation?>(null);
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        string tenantId,
        BlueprintInstallation installation,
        CancellationToken cancellationToken = default)
    {
        var byName = _installations.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, BlueprintInstallation>());
        byName[installation.BlueprintId.Name] = installation;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default)
    {
        if (_installations.TryGetValue(tenantId, out var byName))
        {
            return Task.FromResult(byName.TryRemove(blueprintName, out _));
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(string TenantId, BlueprintInstallation Installation)>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<(string TenantId, BlueprintInstallation Installation)>();
        foreach (var tenantEntry in _installations)
        {
            foreach (var installation in tenantEntry.Value.Values)
            {
                result.Add((tenantEntry.Key, installation));
            }
        }

        return Task.FromResult<IReadOnlyList<(string TenantId, BlueprintInstallation Installation)>>(result);
    }
}
