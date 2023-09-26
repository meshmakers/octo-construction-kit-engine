using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
/// Service for managing the cache of compiled construction kit models.
/// </summary>
public interface ICkCacheService
{
    /// <summary>
    /// Create a new tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    void CreateTenant(string tenantId);

    /// <summary>
    /// Loads a already analyzed model into a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="modelGraph">The ready analyzed graph model</param>
    /// <exception cref="Exception"></exception>
    void LoadCkModelGraph(string tenantId, CkModelGraph modelGraph);

    /// <summary>
    /// Unload a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    void Unload(string tenantId);

    /// <summary>
    /// Returns true if the tenant is loaded
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns></returns>
    bool IsTenantLoaded(string tenantId);

    /// <summary>
    /// Returns a <see cref="CkTypeGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckTypeId">Construction Kit type id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkTypeGraph GetCkType(string tenantId, CkId<CkTypeId> ckTypeId);

    /// <summary>
    /// Returns a <see cref="CkAttributeGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckAttributeId">Construction Kit attribute id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkAttributeGraph GetCkAttribute(string tenantId, CkId<CkAttributeId> ckAttributeId);

    /// <summary>
    /// Returns a <see cref="CkAssociationRoleGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckAssociationRoleId">Construction Kit attribute id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkAssociationRoleGraph GetCkAssociationRole(string tenantId, CkId<CkAssociationRoleId> ckAssociationRoleId);

    /// <summary>
    /// Returns a <see cref="CkRecordGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckRecordId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkRecordGraph GetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId);

    /// <summary>
    /// Returns a <see cref="CkRecordGraph"/> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckEnumId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkEnumGraph GetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId);

    /// <summary>
    /// Saves the cache to a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="stream">Stream ready for write</param>
    /// <exception cref="Exception"></exception>
    Task SaveCacheAsync(string tenantId, Stream stream);

    /// <summary>
    /// Restores the cache from a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="stream">Stream ready for read</param>
    /// <exception cref="Exception"></exception>
    Task RestoreCacheAsync(string tenantId, Stream stream);
    
    /// <summary>
    /// Restores the cache from a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="jsonText">JSON formatted cache representation</param>
    /// <exception cref="Exception"></exception>
    void RestoreCache(string tenantId, string jsonText);
}