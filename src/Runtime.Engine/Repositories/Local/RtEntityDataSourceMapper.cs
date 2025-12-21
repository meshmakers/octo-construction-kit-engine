using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
///     Implementation of <see cref="IDataSourceMapper{TKey,TDocument,TDto}" /> for <see cref="RtEntity" />
/// </summary>
public class RtEntityDataSourceMapper<TDocument> : IDataSourceMapper<OctoObjectId, TDocument, RtEntityTcDto> where TDocument : RtEntity, new()
{
    private readonly ICkCacheService _ckCacheService;
    private readonly IRtRepositorySerializer _rtSerializer;
    private readonly string _tenantId;

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="ckCacheService"></param>
    /// <param name="rtSerializer"></param>
    public RtEntityDataSourceMapper(string tenantId, ICkCacheService ckCacheService, IRtRepositorySerializer rtSerializer)
    {
        _tenantId = tenantId;
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
    }

    /// <inheritdoc />
    public OctoObjectId GetId(RtEntityTcDto dto)
    {
        return dto.RtId;
    }

    /// <inheritdoc />
    public OctoObjectId GetId(TDocument document)
    {
        return document.RtId;
    }

    /// <inheritdoc />
    public TDocument MapToDocument(RtEntityTcDto dto)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public RtEntityTcDto MapToDto(TDocument document)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Apply(TDocument savedDocument, TDocument documentToApply)
    {
        var ckTypeGraph = _ckCacheService.GetRtCkType(_tenantId, savedDocument.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet());
        foreach (var attributeToApply in documentToApply.Attributes)
        {
            if (ckTypeGraph.AllAttributesByName.TryGetValue(attributeToApply.Key, out var ckTypeAttributeGraph))
            {
                savedDocument.SetAttributeValue(ckTypeAttributeGraph.AttributeName, ckTypeAttributeGraph.ValueType,
                    attributeToApply.Value);
            }
        }

        savedDocument.RtChangedDateTime = documentToApply.RtChangedDateTime;
        savedDocument.RtWellKnownName = documentToApply.RtWellKnownName;

        // Apply state changes for archiving
        if (documentToApply.RtState.HasValue)
        {
            savedDocument.RtState = documentToApply.RtState;
        }

        if (documentToApply.RtArchivedDateTime.HasValue)
        {
            savedDocument.RtArchivedDateTime = documentToApply.RtArchivedDateTime;
        }
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, IReadOnlyDictionary<OctoObjectId, TDocument> dictionary)
    {
        await _rtSerializer.SerializeAsync(streamWriter, dictionary.Values).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<OctoObjectId, TDocument>> DeserializeAsync(Stream stream, string locationReference,
        OperationResult operationResult)
    {
        var existingDocuments =
            await _rtSerializer.DeserializeEntitiesAsync(stream, locationReference, operationResult).ConfigureAwait(false);

        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);

        var entities = new ConcurrentDictionary<OctoObjectId, TDocument>();

        Parallel.ForEach(existingDocuments, (modelRtEntity, _) =>
        {
            var ckTypeGraph = _ckCacheService.GetRtCkType(_tenantId, modelRtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet());

            var entity = new TDocument
            {
                RtId = modelRtEntity.RtId,
                CkTypeId = modelRtEntity.CkTypeId,
                RtChangedDateTime = modelRtEntity.RtChangedDateTime,
                RtCreationDateTime = modelRtEntity.RtCreationDateTime,
                RtWellKnownName = modelRtEntity.RtWellKnownName,
                RtState = modelRtEntity.RtState,
                RtArchivedDateTime = modelRtEntity.RtArchivedDateTime
            };

            foreach (var modelRtAttribute in modelRtEntity.Attributes)
            {
                if (ckTypeGraph.AllAttributesByName.TryGetValue(modelRtAttribute.Key, out var ckTypeAttributeGraph))
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