using System;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Tenant-scoped <see cref="IArchiveCoverageProvider"/> (AB#5157) that answers from the
/// process-wide <see cref="ArchiveCoverageCache"/> and measures through
/// <see cref="IStreamDataRepository.GetArchiveCoverageAsync"/> on a miss. Constructed per tenant
/// by the tenant context around the shared cache singleton.
/// </summary>
public sealed class CachedArchiveCoverageProvider : IArchiveCoverageProvider
{
    private readonly string _tenantId;
    private readonly IStreamDataRepository _repository;
    private readonly ArchiveCoverageCache _cache;

    /// <summary>
    /// Creates the provider for one tenant.
    /// </summary>
    /// <param name="tenantId">Tenant whose archives are measured; part of the cache key.</param>
    /// <param name="repository">The tenant's stream data repository, used on a cache miss.</param>
    /// <param name="cache">The process-wide coverage memo shared by every tenant.</param>
    public CachedArchiveCoverageProvider(
        string tenantId,
        IStreamDataRepository repository,
        ArchiveCoverageCache cache)
    {
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public Task<ArchiveCoverage?> GetCoverageAsync(
        OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default)
        => _cache.GetOrFetchAsync(
            _tenantId,
            archiveRtId,
            ct => _repository.GetArchiveCoverageAsync(archiveRtId, ct),
            cancellationToken);
}
