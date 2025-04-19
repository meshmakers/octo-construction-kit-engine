using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
///     Implementation of <see cref="IDataSourceMapper{TKey,TDocument,TDto}" /> for <see cref="RtAssociation" />
/// </summary>
public class BinaryInfoDataSourceMapper : IDataSourceMapper<OctoObjectId, BinaryInfo, BinaryInfoDto>
{
    private readonly IRtRepositorySerializer _rtSerializer;
    private readonly string _tenantId;

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="rtSerializer"></param>
    public BinaryInfoDataSourceMapper(string tenantId, IRtRepositorySerializer rtSerializer)
    {
        _tenantId = tenantId;
        _rtSerializer = rtSerializer;
    }

    /// <inheritdoc />
    public OctoObjectId GetId(BinaryInfo dto)
    {
        return dto.BinaryId;
    }

    /// <inheritdoc />
    public OctoObjectId GetId(BinaryInfoDto document)
    {
        return document.BinaryId;
    }

    /// <inheritdoc />
    public BinaryInfo MapToDocument(BinaryInfoDto dto)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public BinaryInfoDto MapToDto(BinaryInfo document)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void Apply(BinaryInfo savedDocument, BinaryInfo documentToApply)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, IReadOnlyDictionary<OctoObjectId, BinaryInfo> dictionary)
    {
        await _rtSerializer.SerializeAsync(streamWriter, dictionary.Values).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<OctoObjectId, BinaryInfo>> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        var existingDocuments = await _rtSerializer.DeserializeBinaryInfosAsync(stream, locationReference, operationResult)
            .ConfigureAwait(false);

        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);


        return existingDocuments.ToDictionary(k => k.BinaryId, v => v);
    }
}