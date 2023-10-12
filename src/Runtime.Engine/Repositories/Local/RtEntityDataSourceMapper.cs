using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
/// Implementation of <see cref="IDataSourceMapper{TKey,TDocument,TDto}"/> for <see cref="RtEntity"/>
/// </summary>
public class RtEntityDataSourceMapper<TDocument> : IDataSourceMapper<OctoObjectId, TDocument, RtEntityDto> where TDocument: RtEntity, new()
{
    private readonly string _tenantId;
    private readonly ICkCacheService _ckCacheService;
    private readonly IRtSerializer _rtSerializer;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="ckCacheService"></param>
    /// <param name="rtSerializer"></param>
    public RtEntityDataSourceMapper(string tenantId, ICkCacheService ckCacheService, IRtSerializer rtSerializer)
    {
        _tenantId = tenantId;
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
    }
    
    /// <inheritdoc />
    public OctoObjectId GetId(RtEntityDto dto)
    {
        return dto.RtId;
    }

    /// <inheritdoc />
    public OctoObjectId GetId(TDocument document)
    {
        return document.RtId;
    }

    /// <inheritdoc />
    public TDocument MapToDocument(RtEntityDto dto)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public RtEntityDto MapToDto(TDocument document)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Apply(TDocument savedDocument, TDocument documentToApply)
    {
        var ckTypeGraph = _ckCacheService.GetCkType(_tenantId, savedDocument.CkTypeId);
        foreach (var attributeToApply in documentToApply.Attributes)
        {
            if (ckTypeGraph.AllAttributesByName.TryGetValue(attributeToApply.Key, out var ckTypeAttributeGraph))
            {
                savedDocument.SetAttributeValue(ckTypeAttributeGraph.AttributeName, ckTypeAttributeGraph.ValueType,
                    attributeToApply.Value);
            }
        }
       
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, IReadOnlyDictionary<OctoObjectId, TDocument> dictionary)
    {
        var entities = new ConcurrentBag<RtEntityDto>();

        Parallel.ForEach(dictionary.Values, (modelRtEntity, _) =>
        {
            var ckTypeGraph = _ckCacheService.GetCkType(_tenantId, modelRtEntity.CkTypeId);

            var rtEntityDto = new RtEntityDto
            {
                RtId = modelRtEntity.RtId,
                CkTypeId = modelRtEntity.CkTypeId,
                RtChangedDateTime = modelRtEntity.RtChangedDateTime,
                RtCreationDateTime = modelRtEntity.RtCreationDateTime,
                RtWellKnownName = modelRtEntity.RtWellKnownName,
            };
                
            foreach (var modelRtAttribute in modelRtEntity.Attributes)
            {
                if (ckTypeGraph.AllAttributesByName.TryGetValue(modelRtAttribute.Key, out var ckTypeAttributeGraph))
                {
                    rtEntityDto.Attributes.Add(new RtAttributeDto
                    {
                        Id = ckTypeAttributeGraph.CkAttributeId,
                        Value = modelRtAttribute.Value
                    });
                }
            }
                
            entities.Add(rtEntityDto);
        });
            
        var rtModelRoot = new RtModelRootDto
        {
            Dependencies = _ckCacheService.GetCkDependencies(_tenantId).ToList(),
            Entities = entities.ToList()
        };

        await _rtSerializer.SerializeAsync(streamWriter, rtModelRoot).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<OctoObjectId, TDocument>> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        var existingDocuments = await _rtSerializer.DeserializeAsync(stream, locationReference, operationResult).ConfigureAwait(false);
        
        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);

        var entities = new ConcurrentDictionary<OctoObjectId, TDocument>();

        Parallel.ForEach(existingDocuments.Entities, (modelRtEntity, _) =>
        {
            var ckTypeGraph = _ckCacheService.GetCkType(_tenantId, modelRtEntity.CkTypeId);

            var entity = new TDocument
            {
                RtId = modelRtEntity.RtId,
                CkTypeId = modelRtEntity.CkTypeId,
                RtChangedDateTime = modelRtEntity.RtChangedDateTime,
                RtCreationDateTime = modelRtEntity.RtCreationDateTime,
                RtWellKnownName = modelRtEntity.RtWellKnownName,
            };

            foreach (var modelRtAttribute in modelRtEntity.Attributes)
            {
                if (ckTypeGraph.AllAttributes.TryGetValue(modelRtAttribute.Id, out var ckTypeAttributeGraph))
                {
                    entity.SetAttributeValue(ckTypeAttributeGraph.AttributeName, ckTypeAttributeGraph.ValueType,
                        modelRtAttribute.Value);
                }
            }

            entities.TryAdd(modelRtEntity.RtId, entity);
        });

        return entities;
    }
}