using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Caching;

[DebuggerDisplay("{" + nameof(TenantId) + "}")]
internal class CkCache : IDisposable
{
    private readonly ILogger _logger;
    private ICkModelGraph? _modelGraph;

    internal CkCache(ILogger logger, string tenantId)
    {
        _logger = logger;
        TenantId = tenantId;
    }

    public string TenantId { get; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void LoadCkModelGraph(ICkModelGraph modelGraph)
    {
        _logger.LogDebug("Loading model graph into cache for tenant {TenantId}", TenantId);
        _modelGraph = modelGraph;
        _logger.LogDebug("Loading model graph into cache for tenant {TenantId} finished", TenantId);
    }

    public IEnumerable<CkTypeGraph> GetCkTypes()
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.Types.Values;
    }

    public CkTypeGraph GetCkType(CkId<CkTypeId> ckTypeId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Types.TryGetValue(ckTypeId, out var ckTypeGraph))
        {
            throw CkCacheException.CkTypeIdNotFound(TenantId, ckTypeId);
        }

        return ckTypeGraph;
    }

#if NETSTANDARD2_0
    public bool TryGetCkType(CkId<CkTypeId> ckTypeId, out CkTypeGraph? ckTypeGraph)
#else
    public bool TryGetCkType(CkId<CkTypeId> ckTypeId, [NotNullWhen(true)] out CkTypeGraph? ckTypeGraph)
#endif
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Types.TryGetValue(ckTypeId, out ckTypeGraph))
        {
            return false;
        }

        return true;
    }

    public IEnumerable<CkRecordGraph> GetCkRecords()
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.Records.Values;
    }

#if NETSTANDARD2_0
    public bool TryGetCkRecord(CkId<CkRecordId> ckRecordId, out CkRecordGraph? ckRecordGraph)
#else
    public bool TryGetCkRecord(CkId<CkRecordId> ckRecordId, [NotNullWhen(true)] out CkRecordGraph? ckRecordGraph)
#endif
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Records.TryGetValue(ckRecordId, out ckRecordGraph))
        {
            return false;
        }

        return true;
    }
    
#if NETSTANDARD2_0
    public bool TryGetCkEnum(CkId<CkEnumId> ckEnumId, out CkEnumGraph? ckEnumGraph)
#else
    public bool TryGetCkEnum(CkId<CkEnumId> ckEnumId, [NotNullWhen(true)] out CkEnumGraph? ckEnumGraph)
#endif
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Enums.TryGetValue(ckEnumId, out ckEnumGraph))
        {
            return false;
        }

        return true;
    }

    public CkAttributeGraph GetCkAttribute(CkId<CkAttributeId> ckAttributeId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Attributes.TryGetValue(ckAttributeId, out var ckAttributeGraph))
        {
            throw CkCacheException.CkAttributeIdNotFound(TenantId, ckAttributeId);
        }

        return ckAttributeGraph;
    }

    public CkAssociationRoleGraph GetCkAssociationRole(CkId<CkAssociationRoleId> ckAssociationRoleId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.AssociationRoles.TryGetValue(ckAssociationRoleId, out var ckAssociationRoleGraph))
        {
            throw CkCacheException.CkAssociationRoleNotFound(TenantId, ckAssociationRoleId);
        }

        return ckAssociationRoleGraph;
    }

    public CkRecordGraph GetCkRecord(CkId<CkRecordId> ckRecordId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Records.TryGetValue(ckRecordId, out var ckRecordGraph))
        {
            throw CkCacheException.CkRecordNotFound(TenantId, ckRecordId);
        }

        return ckRecordGraph;
    }

    public IEnumerable<CkEnumGraph> GetCkEnums()
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.Enums.Values;
    }


    public CkEnumGraph GetCkEnum(CkId<CkEnumId> ckEnumId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.Enums.TryGetValue(ckEnumId, out var ckEnumGraph))
        {
            throw CkCacheException.CkEnumNotFound(TenantId, ckEnumId);
        }

        return ckEnumGraph;
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _modelGraph = null;
            IsDisposed = true;
        }
    }

    public async Task SaveCacheAsync(Stream stream)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        var options = GetJsonSerializerOptions();

        await JsonSerializer.SerializeAsync(stream, _modelGraph.ToCkCacheRoot(), options).ConfigureAwait(false);
    }

    public async Task RestoreCacheAsync(Stream stream)
    {
        var options = GetJsonSerializerOptions();

        var ckCacheRoot = await JsonSerializer.DeserializeAsync<CkCacheRoot>(stream, options).ConfigureAwait(false);
        if (ckCacheRoot == null)
        {
            throw CkCacheException.CannotDeserializeCache(TenantId);
        }

        _modelGraph = new CkModelGraph(ckCacheRoot);
    }

    public void RestoreCache(string jsonText)
    {
        var byteArray = Encoding.UTF8.GetBytes(jsonText);
        using var memStream = new MemoryStream(byteArray);

        var options = GetJsonSerializerOptions();

        var ckCacheRoot = JsonSerializer.Deserialize<CkCacheRoot>(memStream, options);
        if (ckCacheRoot == null)
        {
            throw CkCacheException.CannotDeserializeCache(TenantId);
        }

        _modelGraph = new CkModelGraph(ckCacheRoot);
    }

    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitCkV2Defaults } }
        };
        options.Converters.Add(new CkIdAttributeIdConverter());
        options.Converters.Add(new CkIdAssociationRoleIdConverter());
        options.Converters.Add(new CkIdTypeIdConverter());
        options.Converters.Add(new CkIdRecordIdConverter());
        options.Converters.Add(new CkIdEnumIdConverter());
        options.Converters.Add(new CkIdInterfaceIdConverter());

        options.Converters.Add(new CkModelIdConverter());
        return options;
    }

    /// <summary>
    ///     CK v2 (AB#5584): the members added for interfaces, methods and attribute access are omitted from the cache
    ///     JSON while they hold their default (empty / <see cref="CkAttributeAccessDto.ReadWrite" />), so the cache of a
    ///     <c>ckLanguage: 1</c> model stays byte-identical to the one written before CK v2. Reading tolerates the
    ///     missing keys (defaulted constructor parameters / init setters).
    /// </summary>
    private static void OmitCkV2Defaults(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            if (typeInfo.Type == typeof(CkTypeGraph) && property.Name is "declaredImplements"
                    or "allImplementedInterfaces" or "definedMethods" or "allMethods" ||
                typeInfo.Type == typeof(CkCacheRoot) && property.Name == "interfaces")
            {
                property.ShouldSerialize = (_, value) => value is IEnumerable enumerable && enumerable.GetEnumerator().MoveNext();
            }
            else if (typeInfo.Type == typeof(CkTypeAttributeGraph) && property.Name == "access")
            {
                property.ShouldSerialize = (_, value) => value is CkAttributeAccessDto access &&
                                                         access != CkAttributeAccessDto.ReadWrite;
            }
        }
    }

    public ICollection<CkModelId> GetCkModelIds()
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.Dependencies.Select(x => x.Key).Distinct().ToList();
    }

    public ICollection<CkModelId> EnsureModelIds(IEnumerable<CkModelId> ckModelIds)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        // check if dependent ck models are existing.
        List<CkModelId> missingModelIds = [];
        foreach (var ckModelId in ckModelIds)
        {
            if (!_modelGraph.Dependencies.ContainsKey(ckModelId))
            {
                missingModelIds.Add(ckModelId);
            }
        }

        return missingModelIds;
    }

    public ICollection<CkModelIdVersionRange> EnsureModelIdRanges(IEnumerable<CkModelIdVersionRange> modelIdRanges)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        var loadedModelIds = _modelGraph.Dependencies.Select(x => x.Key).Distinct().ToList();
        List<CkModelIdVersionRange> unsatisfiedRanges = [];
        foreach (var range in modelIdRanges)
        {
            if (!loadedModelIds.Any(range.IsSatisfiedBy))
            {
                unsatisfiedRanges.Add(range);
            }
        }

        return unsatisfiedRanges;
    }

    public IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPaths(CkId<CkTypeId> ckTypeId,
        CkTypeQueryColumnOptions? options = null)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.GetCkTypeQueryColumnPaths(ckTypeId, options);
    }

    public IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPathsByRtCkId(RtCkId<CkTypeId> rtCkTypeId, CkTypeQueryColumnOptions? options = null)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.GetCkTypeQueryColumnPathsByRtCkId(rtCkTypeId, options);
    }

    public CkTypeGraph GetRtCkType(RtCkId<CkTypeId> rtCkTypeId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.TypesByRtCk.TryGetValue(rtCkTypeId, out var ckTypeGraph))
        {
            throw CkCacheException.RtCkTypeIdNotFound(TenantId, rtCkTypeId);
        }

        return ckTypeGraph;
    }

#if NETSTANDARD2_0
    public bool TryGetRtCkType(RtCkId<CkTypeId> rtCkTypeId, out CkTypeGraph ckTypeGraph)
#else
    public bool TryGetRtCkType(RtCkId<CkTypeId> rtCkTypeId, [NotNullWhen(true)] out CkTypeGraph? ckTypeGraph)
#endif
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.TypesByRtCk.TryGetValue(rtCkTypeId, out ckTypeGraph))
        {
            return false;
        }

        return true;
    }

#if NETSTANDARD2_0
    public bool TryGetRtCkRecord(RtCkId<CkRecordId> rtCkRecordId, out CkRecordGraph ckRecordGraph)
#else
    public bool TryGetRtCkRecord(RtCkId<CkRecordId> rtCkRecordId, [NotNullWhen(true)] out CkRecordGraph? ckRecordGraph)
#endif
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.RecordsByRtCk.TryGetValue(rtCkRecordId, out ckRecordGraph))
        {
            return false;
        }

        return true;
    }


#if NETSTANDARD2_0
    public bool TryGetRtCkEnum(RtCkId<CkEnumId> rtCkEnumId, out CkEnumGraph? ckEnumGraph)
#else
    public bool TryGetRtCkEnum(RtCkId<CkEnumId> rtCkEnumId, [NotNullWhen(true)] out CkEnumGraph? ckEnumGraph)
#endif
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.EnumsByRtCk.TryGetValue(rtCkEnumId, out ckEnumGraph))
        {
            return false;
        }

        return true;
    }

    public CkAttributeGraph GetRtCkAttribute(RtCkId<CkAttributeId> rtCkAttributeId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.AttributesByRtCk.TryGetValue(rtCkAttributeId, out var ckAttributeGraph))
        {
            throw CkCacheException.RtCkAttributeIdNotFound(TenantId, rtCkAttributeId);
        }

        return ckAttributeGraph;
    }

    public CkAssociationRoleGraph GetRtCkAssociationRole(RtCkId<CkAssociationRoleId> rtCkAssociationRoleId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.AssociationRolesByRtCk.TryGetValue(rtCkAssociationRoleId, out var ckAssociationRoleGraph))
        {
            throw CkCacheException.RtCkAssociationRoleNotFound(TenantId, rtCkAssociationRoleId);
        }

        return ckAssociationRoleGraph;
    }

    public CkRecordGraph GetRtCkRecord(RtCkId<CkRecordId> rtCkRecordId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.RecordsByRtCk.TryGetValue(rtCkRecordId, out var ckRecordGraph))
        {
            throw CkCacheException.RtCkRecordNotFound(TenantId, rtCkRecordId);
        }

        return ckRecordGraph;
    }

    public CkInterfaceGraph GetRtCkInterface(RtCkId<CkInterfaceId> rtCkInterfaceId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.InterfacesByRtCk.TryGetValue(rtCkInterfaceId, out var ckInterfaceGraph))
        {
            throw CkCacheException.RtCkInterfaceNotFound(TenantId, rtCkInterfaceId);
        }

        return ckInterfaceGraph;
    }

    public IReadOnlyCollection<CkInterfaceGraph> GetCkInterfaces()
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.Interfaces.Values.ToList();
    }

    public CkEnumGraph GetRtCkEnum(RtCkId<CkEnumId> rtCkEnumId)
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        if (!_modelGraph.EnumsByRtCk.TryGetValue(rtCkEnumId, out var ckEnumGraph))
        {
            throw CkCacheException.RtCkEnumNotFound(TenantId, rtCkEnumId);
        }

        return ckEnumGraph;
    }


}