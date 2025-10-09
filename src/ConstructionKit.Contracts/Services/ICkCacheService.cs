using System.Diagnostics.CodeAnalysis;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Service for managing the cache of compiled construction kit models.
/// </summary>
public interface ICkCacheService
{
    /// <summary>
    ///     Create a new tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    void CreateTenant(string tenantId);

    /// <summary>
    ///     Loads a already analyzed model into a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="modelGraph">The ready analyzed graph model</param>
    /// <exception cref="Exception"></exception>
    void LoadCkModelGraph(string tenantId, ICkModelGraph modelGraph);

    /// <summary>
    ///     Returns the construction kit model library ids for a tenant.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns>A list of dependencies</returns>
    ICollection<CkModelId> GetCkModelIds(string tenantId);
    
    /// <summary>
    /// Ensures that the given model ids are available in the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="modelIds">The model ids to ensure</param>
    /// <returns>A list of models that are not existing</returns>
    ICollection<CkModelId> EnsureModelIds(string tenantId, IEnumerable<CkModelId> modelIds);

    /// <summary>
    ///     Unload a tenant cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    void Unload(string tenantId);

    /// <summary>
    ///     Returns true if the tenant is loaded
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns></returns>
    bool IsTenantLoaded(string tenantId);

    /// <summary>
    ///     Returns all available <see cref="CkTypeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns></returns>
    public IEnumerable<CkTypeGraph> GetCkTypes(string tenantId);

    /// <summary>
    ///     Returns a <see cref="CkTypeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckTypeId">Construction Kit type id.</param>
    /// <returns></returns>
    CkTypeGraph GetCkType(string tenantId, CkId<CkTypeId> ckTypeId);

    /// <summary>
    ///     Returns a <see cref="CkTypeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkTypeId">Construction Kit type id.</param>
    /// <returns></returns>
    CkTypeGraph GetRtCkType(string tenantId, RtCkId<CkTypeId> rtCkTypeId);

    /// <summary>
    ///     Returns a <see cref="CkTypeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckTypeId">Construction Kit type id.</param>
    /// <param name="ckTypeGraph">Returns the ck type graph</param>
    /// <returns>True, when the given ck type id exists</returns>
    /// <exception cref="Exception"></exception>
#if NETSTANDARD2_0
    bool TryGetCkType(string tenantId, CkId<CkTypeId> ckTypeId, out CkTypeGraph? ckTypeGraph);
#else
    bool TryGetCkType(string tenantId, CkId<CkTypeId> ckTypeId, [NotNullWhen(true)] out CkTypeGraph? ckTypeGraph);
#endif

    /// <summary>
    ///     Returns a <see cref="CkTypeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkTypeId">Construction Kit type id.</param>
    /// <param name="ckTypeGraph">Returns the ck type graph</param>
    /// <returns>True, when the given ck type id exists</returns>
    /// <exception cref="Exception"></exception>
#if NETSTANDARD2_0
    bool TryGetRtCkType(string tenantId, RtCkId<CkTypeId> rtCkTypeId, out CkTypeGraph? ckTypeGraph);
#else
    bool TryGetRtCkType(string tenantId, RtCkId<CkTypeId> rtCkTypeId, [NotNullWhen(true)] out CkTypeGraph? ckTypeGraph);
#endif

    /// <summary>
    ///     Returns all available <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns></returns>
    public IEnumerable<CkRecordGraph> GetCkRecords(string tenantId);

    /// <summary>
    ///     Returns a <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckRecordId">Construction Kit record id.</param>
    /// <param name="ckRecordGraph">Returns the ck record graph</param>
    /// <returns>True, when the given ck record id exists</returns>
    /// <exception cref="Exception"></exception>
#if NETSTANDARD2_0
    bool TryGetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId, out CkRecordGraph? ckRecordGraph);
#else
    bool TryGetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId, [NotNullWhen(true)] out CkRecordGraph? ckRecordGraph);
#endif

    /// <summary>
    ///     Returns a <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkRecordId">Construction Kit record id.</param>
    /// <param name="ckRecordGraph">Returns the ck record graph</param>
    /// <returns>True, when the given ck record id exists</returns>
    /// <exception cref="Exception"></exception>
#if NETSTANDARD2_0
    bool TryGetRtCkRecord(string tenantId, RtCkId<CkRecordId> rtCkRecordId, out CkRecordGraph? ckRecordGraph);
#else
    bool TryGetRtCkRecord(string tenantId, RtCkId<CkRecordId> rtCkRecordId, [NotNullWhen(true)] out CkRecordGraph? ckRecordGraph);
#endif


    /// <summary>
    ///     Returns a <see cref="CkEnumGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckEnumId">Construction Kit enum id.</param>
    /// <param name="ckEnumGraph">Returns the ck enum graph</param>
    /// <returns>True, when the given ck enum id exists</returns>
    /// <exception cref="Exception"></exception>
#if NETSTANDARD2_0
    bool TryGetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId, out CkEnumGraph? ckEnumGraph);
#else
    bool TryGetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId, [NotNullWhen(true)] out CkEnumGraph? ckEnumGraph);
#endif

    /// <summary>
    ///     Returns a <see cref="CkEnumGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkEnumId">Construction Kit enum id.</param>
    /// <param name="ckEnumGraph">Returns the ck enum graph</param>
    /// <returns>True, when the given ck enum id exists</returns>
    /// <exception cref="Exception"></exception>
#if NETSTANDARD2_0
    bool TryGetRtCkEnum(string tenantId, RtCkId<CkEnumId> rtCkEnumId, out CkEnumGraph? ckEnumGraph);
#else
    bool TryGetRtCkEnum(string tenantId, RtCkId<CkEnumId> rtCkEnumId, [NotNullWhen(true)] out CkEnumGraph? ckEnumGraph);
#endif


    /// <summary>
    ///     Returns a <see cref="CkAttributeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckAttributeId">Construction Kit attribute id.</param>
    /// <returns></returns>
    CkAttributeGraph GetCkAttribute(string tenantId, CkId<CkAttributeId> ckAttributeId);

    /// <summary>
    ///     Returns a <see cref="CkAttributeGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkAttributeId">Construction Kit attribute id.</param>
    /// <returns></returns>
    CkAttributeGraph GetRtCkAttribute(string tenantId, RtCkId<CkAttributeId> rtCkAttributeId);

    /// <summary>
    ///     Returns a <see cref="CkAssociationRoleGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckAssociationRoleId">Construction Kit attribute id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkAssociationRoleGraph GetCkAssociationRole(string tenantId, CkId<CkAssociationRoleId> ckAssociationRoleId);

    /// <summary>
    ///     Returns a <see cref="CkAssociationRoleGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkAssociationRoleId">Construction Kit attribute id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkAssociationRoleGraph GetRtCkAssociationRole(string tenantId, RtCkId<CkAssociationRoleId> rtCkAssociationRoleId);

    /// <summary>
    ///     Returns a <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckRecordId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkRecordGraph GetCkRecord(string tenantId, CkId<CkRecordId> ckRecordId);

    /// <summary>
    ///     Returns a <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkRecordId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkRecordGraph GetRtCkRecord(string tenantId, RtCkId<CkRecordId> rtCkRecordId);

    /// <summary>
    ///     Returns all available <see cref="CkEnumGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <returns></returns>
    public IEnumerable<CkEnumGraph> GetCkEnums(string tenantId);

    /// <summary>
    ///     Returns a <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="ckEnumId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkEnumGraph GetCkEnum(string tenantId, CkId<CkEnumId> ckEnumId);

    /// <summary>
    ///     Returns a <see cref="CkRecordGraph" /> from the cache.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance.</param>
    /// <param name="rtCkEnumId">Construction Kit record id.</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    CkEnumGraph GetRtCkEnum(string tenantId, RtCkId<CkEnumId> rtCkEnumId);

    /// <summary>
    ///     Saves the cache to a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="stream">Stream ready for write</param>
    /// <exception cref="Exception"></exception>
    Task SaveCacheAsync(string tenantId, Stream stream);

    /// <summary>
    ///     Restores the cache from a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="stream">Stream ready for read</param>
    /// <exception cref="Exception"></exception>
    Task RestoreCacheAsync(string tenantId, Stream stream);

    /// <summary>
    ///     Restores the cache from a stream.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="jsonText">JSON formatted cache representation</param>
    /// <exception cref="Exception"></exception>
    void RestoreCache(string tenantId, string jsonText);

    /// <summary>
    ///     Returns the construction kit model library ids for a tenant.
    /// </summary>
    /// <param name="tenantId">Unique name of the tenant within Octo Instance</param>
    /// <param name="ckTypeId">The construction kit type id</param>
    /// <param name="ignoreNavigationProperties">When true, the navigation properties are ignored</param>
    IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPaths(string tenantId, CkId<CkTypeId> ckTypeId, bool ignoreNavigationProperties = false);
}