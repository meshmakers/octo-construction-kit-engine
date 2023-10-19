using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
/// Implementation of <see cref="IDataSourceMapper{TKey,TDocument,TDto}"/> for <see cref="RtAssociation"/>
/// </summary>
public class RtAssociationDataSourceMapper: IDataSourceMapper<OctoObjectId, RtAssociation, RtAssociationDto>
{
    private readonly string _tenantId;
    private readonly ICkCacheService _ckCacheService;
    private readonly IRtRepositorySerializer _rtSerializer;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="ckCacheService"></param>
    /// <param name="rtSerializer"></param>
    public RtAssociationDataSourceMapper(string tenantId, ICkCacheService ckCacheService, IRtRepositorySerializer rtSerializer)
    {
        _tenantId = tenantId;
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
    }
    
    /// <inheritdoc />
    public OctoObjectId GetId(RtAssociationDto dto)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public OctoObjectId GetId(RtAssociation document)
    {
        return document.AssociationId;
    }

    /// <inheritdoc />
    public RtAssociation MapToDocument(RtAssociationDto dto)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public RtAssociationDto MapToDto(RtAssociation document)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Apply(RtAssociation savedDocument, RtAssociation documentToApply)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, IReadOnlyDictionary<OctoObjectId, RtAssociation> dictionary)
    {
        await _rtSerializer.SerializeAsync(streamWriter, dictionary.Values).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<OctoObjectId, RtAssociation>> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        var existingDocuments = await _rtSerializer.DeserializeAssociationsAsync(stream, locationReference, operationResult).ConfigureAwait(false);

        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);

        
        return existingDocuments.ToDictionary(k => k.AssociationId, v=> v);
    }
}