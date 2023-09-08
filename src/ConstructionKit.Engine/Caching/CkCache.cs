using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

namespace Meshmakers.Octo.ConstructionKit.Engine.Caching;

[DebuggerDisplay("{" + nameof(TenantId) + "}")]
internal class CkCache : IDisposable
{
    private readonly IModelResolver _modelResolver;
    private CkModelGraph? _modelGraph;
    
    internal CkCache(string tenantId, IModelResolver modelResolver)
    {
        _modelResolver = modelResolver;
        TenantId = tenantId;
    }

    public string TenantId { get; }

    public async Task LoadCkModelAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult)
    {
        _modelGraph = await _modelResolver.ResolveAsync(compiledModel, operationResult);
    }
    
    public void LoadCkModelGraph(CkModelGraph modelGraph)
    {
        _modelGraph = modelGraph;
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

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new CkIdAttributeIdConverter(), 
                new CkIdAssociationIdConverter(), 
                new CkIdTypeIdConverter(),
                new CkIdRecordIdConverter(),
                new CkIdEnumIdConverter(),
                new CkModelIdConverter()
            }
        };

        await JsonSerializer.SerializeAsync(stream, _modelGraph.ToCkCacheRoot(), options);
    }

    public async Task RestoreCacheAsync(Stream stream)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new CkIdAttributeIdConverter(), 
                new CkIdAssociationIdConverter(), 
                new CkIdTypeIdConverter(),
                new CkIdRecordIdConverter(),
                new CkIdEnumIdConverter(),
                new CkModelIdConverter()
            }
        };

        var ckCacheRoot = await JsonSerializer.DeserializeAsync<CkCacheRoot>(stream, options);
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

        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new CkIdAttributeIdConverter(), 
                new CkIdAssociationIdConverter(), 
                new CkIdTypeIdConverter(),
                new CkIdRecordIdConverter(),
                new CkIdEnumIdConverter(),
                new CkModelIdConverter()
            }
        };

        var ckCacheRoot = JsonSerializer.Deserialize<CkCacheRoot>(memStream, options);
        if (ckCacheRoot == null)
        {
            throw CkCacheException.CannotDeserializeCache(TenantId);
        }

        _modelGraph = new CkModelGraph(ckCacheRoot);
    }
}