using System.Collections.Concurrent;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// In-memory implementation of tenant blueprint history.
/// This is suitable for testing and scenarios where persistence is handled externally.
/// For production use, implement a persistent storage backend.
/// </summary>
internal class InMemoryTenantBlueprintHistory : ITenantBlueprintHistory
{
    private readonly ConcurrentDictionary<string, List<TenantBlueprintInfo>> _history = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<TenantBlueprintInfo>> GetHistoryAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_history.TryGetValue(tenantId, out var history))
        {
            return Task.FromResult<IReadOnlyList<TenantBlueprintInfo>>(
                history.OrderBy(h => h.AppliedAt).ToList());
        }

        return Task.FromResult<IReadOnlyList<TenantBlueprintInfo>>(
            Array.Empty<TenantBlueprintInfo>());
    }

    /// <inheritdoc />
    public Task<TenantBlueprintInfo?> GetCurrentAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_history.TryGetValue(tenantId, out var history) && history.Count > 0)
        {
            return Task.FromResult<TenantBlueprintInfo?>(
                history.OrderByDescending(h => h.AppliedAt).First());
        }

        return Task.FromResult<TenantBlueprintInfo?>(null);
    }

    /// <inheritdoc />
    public Task AddEntryAsync(
        string tenantId,
        TenantBlueprintInfo info,
        CancellationToken cancellationToken = default)
    {
        _history.AddOrUpdate(
            tenantId,
            _ => [info],
            (_, existing) =>
            {
                existing.Add(info);
                return existing;
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasBlueprintAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _history.TryGetValue(tenantId, out var history) && history.Count > 0);
    }
}
