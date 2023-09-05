using System.Collections.Concurrent;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Caching;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
/// Service for managing the cache of compiled construction kit models.
/// </summary>
public class CkCacheService : ICkCacheService
{
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IInheritanceResolver _inheritanceResolver;
    private readonly IElementResolver _elementResolver;
    private readonly ConcurrentDictionary<string, CkCache> _ckCaches;

    /// <summary>
    /// Creates a new instance of the <see cref="CkCacheService"/> class.
    /// </summary>
    /// <param name="dependencyResolver"></param>
    /// <param name="inheritanceResolver"></param>
    /// <param name="elementResolver"></param>
    public CkCacheService(IDependencyResolver dependencyResolver, IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver)
    {
        _dependencyResolver = dependencyResolver;
        _inheritanceResolver = inheritanceResolver;
        _elementResolver = elementResolver;
        _ckCaches = new ConcurrentDictionary<string, CkCache>();
    }

    /// <summary>
    /// Create a new tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    public void CreateTenant(string tenantId)
    {
        _ckCaches[tenantId] = new CkCache(tenantId, _dependencyResolver, _inheritanceResolver,
            _elementResolver);
    }

    /// <summary>
    /// Loads a compiled model into a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="compiledModel">The compiled construction kit model</param>
    /// <param name="operationResult">Validation results during schema validation and model validation</param>
    /// <exception cref="Exception"></exception>
    public async Task LoadCkModelAsync(string tenantId, CkCompiledModelRoot compiledModel, OperationResult operationResult)
    {
        if (!_ckCaches.TryGetValue(tenantId, out var ckCache))
        {
            throw CkCacheException.CkCacheNotFound(tenantId);
        }
        
        await ckCache.LoadCkModelAsync(compiledModel, operationResult);
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
        
        await ckCache.SaveCacheAsync(stream);
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
        
        await ckCache.RestoreCacheAsync(stream);
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