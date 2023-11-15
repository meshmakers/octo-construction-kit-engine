using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Caching;

[DebuggerDisplay("{" + nameof(TenantId) + "}")]
internal class CkCache : IDisposable
{
    private readonly ILogger _logger;
    private CkModelGraph? _modelGraph;

    internal CkCache(ILogger logger, string tenantId)
    {
        _logger = logger;
        TenantId = tenantId;
    }

    public string TenantId { get; }

    public void LoadCkModelGraph(CkModelGraph modelGraph)
    {
        _logger.LogInformation("Loading model graph into cache for tenant {TenantId}", TenantId);
        _modelGraph = modelGraph;
        _logger.LogInformation("Loading model graph into cache for tenant {TenantId} finished", TenantId);
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

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(jsonText);
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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new CkIdAttributeIdConverter());
        options.Converters.Add(new CkIdAssociationRoleIdConverter());
        options.Converters.Add(new CkIdTypeIdConverter());
        options.Converters.Add(new CkIdRecordIdConverter());
        options.Converters.Add(new CkIdEnumIdConverter());
        options.Converters.Add(new CkModelIdConverter());
        return options;
    }

    public ICollection<CkModelId> GetCkDependencies()
    {
        if (_modelGraph == null)
        {
            throw CkCacheException.CacheUnloaded(TenantId);
        }

        return _modelGraph.Dependencies.SelectMany(x => x.Value).Distinct().ToList();
    }
}