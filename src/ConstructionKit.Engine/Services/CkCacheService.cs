using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Caching;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
///     Service for managing the cache of compiled construction kit models.
/// </summary>
public class CkCacheService : ICkCacheService
{
    private readonly ConcurrentDictionary<string, CkCache> _ckCaches;
    private readonly ILogger<CkCacheService> _logger;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkCacheService" /> class.
    /// </summary>
    /// <param name="logger">Instance of the logger interface</param>
    public CkCacheService(ILogger<CkCacheService> logger)
    {
        _logger = logger;
        _ckCaches = new ConcurrentDictionary<string, CkCache>();
    }

    /// <inheritdoc />
    public void CreateTenant(string tenantId)
    {
        _ckCaches[tenantId] = new CkCache(_logger, tenantId);
    }

    /// <inheritdoc />
    public void LoadCkModelGraph(string tenantId, ICkModelGraph modelGraph)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        ckCache.LoadCkModelGraph(modelGraph);
    }

    /// <inheritdoc />
    public ICollection<CkModelId> GetCkModelIds(string tenantId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkModelIds();
    }

    /// <inheritdoc />
    public ICollection<CkModelId> EnsureModelIds(string tenantId, IEnumerable<CkModelId> modelIds)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.EnsureModelIds(modelIds);
    }

    /// <inheritdoc />
    public void Unload(string tenantId)
    {
        if (!_ckCaches.TryRemove(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        ckCache.Dispose();
    }

    /// <inheritdoc />
    public bool IsTenantLoaded(string tenantId)
    {
        return _ckCaches.ContainsKey(tenantId);
    }


    /// <inheritdoc />
    public IEnumerable<CkTypeGraph> GetCkTypes(string tenantId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkTypes();
    }

    /// <inheritdoc />
    public CkTypeGraph GetCkType(string tenantId, CkId<CkTypeId> ckTypeId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkType(ckTypeId);
    }

    /// <inheritdoc />
#if NETSTANDARD2_0
    public bool TryGetCkType(string tenantId, CkId<CkTypeId> ckTypeId, out CkTypeGraph? ckTypeGraph)
#else
    public bool TryGetCkType(string tenantId, CkId<CkTypeId> ckTypeId, [NotNullWhen(true)] out CkTypeGraph? ckTypeGraph)
#endif
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            ckTypeGraph = null;
            return false;
        }

        return ckCache.TryGetCkType(ckTypeId, out ckTypeGraph);
    }

    /// <inheritdoc />
    public IEnumerable<CkRecordGraph> GetCkRecords(string tenantId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkRecords();
    }

    /// <inheritdoc />
#if NETSTANDARD2_0
    public bool TryGetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId, out CkRecordGraph? ckRecordGraph)
#else
    public bool TryGetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId, [NotNullWhen(true)] out CkRecordGraph? ckRecordGraph)
#endif
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            ckRecordGraph = null;
            return false;
        }

        return ckCache.TryGetCkRecord(ckRecordId, out ckRecordGraph);
    }

    /// <inheritdoc />
#if NETSTANDARD2_0
    public bool TryGetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId, out CkEnumGraph? ckEnumGraph)
#else
    public bool TryGetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId, [NotNullWhen(true)] out CkEnumGraph? ckEnumGraph)
#endif
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            ckEnumGraph = null;
            return false;
        }

        return ckCache.TryGetCkEnum(ckEnumId, out ckEnumGraph);
    }

    /// <inheritdoc />
    public CkAttributeGraph GetCkAttribute(string tenantId, CkId<CkAttributeId> ckAttributeId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkAttribute(ckAttributeId);
    }

    /// <inheritdoc />
    public CkAssociationRoleGraph GetCkAssociationRole(string tenantId, CkId<CkAssociationRoleId> ckAssociationRoleId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkAssociationRole(ckAssociationRoleId);
    }

    /// <inheritdoc />
    public CkRecordGraph GetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkRecord(ckRecordId);
    }

    /// <inheritdoc />
    public IEnumerable<CkEnumGraph> GetCkEnums(string tenantId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkEnums();
    }

    /// <inheritdoc />
    public CkEnumGraph GetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkEnum(ckEnumId);
    }

    /// <inheritdoc />
    public async Task SaveCacheAsync(string tenantId, Stream stream)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        await ckCache.SaveCacheAsync(stream).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RestoreCacheAsync(string tenantId, Stream stream)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        await ckCache.RestoreCacheAsync(stream).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void RestoreCache(string tenantId, string jsonText)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        ckCache.RestoreCache(jsonText);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPaths(string tenantId, CkId<CkTypeId> ckTypeId, bool ignoreNavigationProperties = false)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkTypeQueryColumnPaths(ckTypeId, ignoreNavigationProperties);
    }
}