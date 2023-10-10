using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Caching;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
/// Service for managing the cache of compiled construction kit models.
/// </summary>
public class CkCacheService : ICkCacheService
{
    private readonly ILogger<CkCacheService> _logger;
    private readonly ConcurrentDictionary<string, CkCache> _ckCaches;

    /// <summary>
    /// Creates a new instance of the <see cref="CkCacheService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the logger interface</param>
    public CkCacheService(ILogger<CkCacheService> logger)
    {
        _logger = logger;
        _ckCaches = new ConcurrentDictionary<string, CkCache>();
    }

    /// <summary>
    /// Create a new tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    public void CreateTenant(string tenantId)
    {
        _ckCaches[tenantId] = new CkCache(_logger, tenantId);
    }

    /// <summary>
    /// Loads a already analyzed model into a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="modelGraph">The ready analyzed graph model</param>
    /// <exception cref="Exception"></exception>
    public void LoadCkModelGraph(string tenantId, CkModelGraph modelGraph)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        ckCache.LoadCkModelGraph(modelGraph);
    }

    /// <inheritdoc />
    public ICollection<CkModelId> GetCkDependencies(string tenantId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }

        return ckCache.GetCkDependencies();
    }

    /// <summary>
    /// Unload a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    public void Unload(string tenantId)
    {
        if (!_ckCaches.TryRemove(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        ckCache.Dispose();
    }

    /// <summary>
    /// Returns true if the tenant is loaded
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns></returns>
    public bool IsTenantLoaded(string tenantId)
    {
        return _ckCaches.ContainsKey(tenantId);
    }

    /// <summary>
    /// Returns a <see cref="CkTypeGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckTypeId">Construction Kit type id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
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

    /// <summary>
    /// Returns a <see cref="CkAttributeGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckAttributeId">Construction Kit attribute id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public CkAttributeGraph GetCkAttribute(string tenantId, CkId<CkAttributeId> ckAttributeId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        return ckCache.GetCkAttribute(ckAttributeId);
    }
    
    /// <summary>
    /// Returns a <see cref="CkAssociationRoleGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckAssociationRoleId">Construction Kit attribute id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public CkAssociationRoleGraph GetCkAssociationRole(string tenantId, CkId<CkAssociationRoleId> ckAssociationRoleId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        return ckCache.GetCkAssociationRole(ckAssociationRoleId);
    }
    
    /// <summary>
    /// Returns a <see cref="CkRecordGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckRecordId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public CkRecordGraph GetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        return ckCache.GetCkRecord(ckRecordId);
    }
    
    /// <summary>
    /// Returns a <see cref="CkRecordGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckEnumId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public CkEnumGraph GetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        return ckCache.GetCkEnum(ckEnumId);
    }
    
    /// <summary>
    /// Saves the cache to a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="stream">Stream ready for write</param>
    /// <exception cref="Exception"></exception>
    public async Task SaveCacheAsync(string tenantId, Stream stream)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        await ckCache.SaveCacheAsync(stream).ConfigureAwait(false);
    }
    
    /// <summary>
    /// Restores the cache from a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="stream">Stream ready for read</param>
    /// <exception cref="Exception"></exception>
    public async Task RestoreCacheAsync(string tenantId, Stream stream)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        await ckCache.RestoreCacheAsync(stream).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores the cache from a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="jsonText">JSON formatted cache representation</param>
    /// <exception cref="Exception"></exception>
    public void RestoreCache(string tenantId, string jsonText)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        ckCache.RestoreCache(jsonText);
    }
}