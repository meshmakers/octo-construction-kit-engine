using System.Collections.Concurrent;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.CkModelMigrations;

/// <summary>
/// Default implementation of <see cref="IRuntimeRepositoryProvider"/> that manages
/// runtime repositories for tenants.
/// </summary>
/// <remarks>
/// This implementation allows manual registration of repositories for tenants.
/// In production scenarios, this would typically be replaced with an implementation
/// that retrieves repositories from a factory or the DI container.
/// </remarks>
internal class RuntimeRepositoryProvider : IRuntimeRepositoryProvider
{
    private readonly ILogger<RuntimeRepositoryProvider> _logger;
    private readonly ConcurrentDictionary<string, IRuntimeRepository> _repositories = new();

    /// <summary>
    /// Creates a new instance of <see cref="RuntimeRepositoryProvider"/>
    /// </summary>
    public RuntimeRepositoryProvider(ILogger<RuntimeRepositoryProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a runtime repository for a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier</param>
    /// <param name="repository">The repository instance</param>
    public void RegisterRepository(string tenantId, IRuntimeRepository repository)
    {
        _repositories[tenantId] = repository;
        _logger.LogDebug("Registered repository for tenant {TenantId}", tenantId);
    }

    /// <summary>
    /// Unregisters a runtime repository for a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier</param>
    public void UnregisterRepository(string tenantId)
    {
        _repositories.TryRemove(tenantId, out _);
        _logger.LogDebug("Unregistered repository for tenant {TenantId}", tenantId);
    }

    /// <inheritdoc />
    public Task<IRuntimeRepository?> GetRepositoryAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        _repositories.TryGetValue(tenantId, out var repository);
        return Task.FromResult<IRuntimeRepository?>(repository);
    }

    /// <inheritdoc />
    public bool IsRepositoryAvailable(string tenantId)
    {
        return _repositories.ContainsKey(tenantId);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetSchemaVersionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        // This simple implementation doesn't track schema versions
        // The MongoDB implementation provides the actual schema version lookup
        return Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>());
    }
}
