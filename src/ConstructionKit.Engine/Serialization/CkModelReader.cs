using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

/// <summary>
/// Reads a CK model from a file.
/// </summary>
public class CkModelReader
{
    private readonly ICkValidationService _ckValidationService;
    private readonly ILogger<CkModelReader> _logger;
    private readonly ICkSerializer _ckSerializer;

    /// <summary>
    /// Creates a new instance of the <see cref="CkModelReader"/> class.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="ckSerializer"></param>
    /// <param name="ckValidationService"></param>
    public CkModelReader(ILogger<CkModelReader> logger, ICkSerializer ckSerializer, ICkValidationService ckValidationService)
    {
        _ckValidationService = ckValidationService;
        _logger = logger;
        _ckSerializer = ckSerializer;
    }

    /// <summary>
    /// Reads a CK model from a file.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="operationResult"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="Exception"></exception>
    public async Task ReadAsync(string filePath, OperationResult operationResult, CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Reading CK model...");

        CkCompiledModelRoot? model;

        try
        {
#if NETSTANDARD2_0
            using var stream = File.OpenRead(filePath);
#else
            await using var stream = File.OpenRead(filePath);
#endif
            model = await _ckSerializer.DeserializeCompiledModelRootAsync(stream, operationResult);

            if (model == null)
            {
                throw ModelParseException.CannotDeserializeModel(filePath);
            }
        }
        catch (Exception e)
        {
            throw ModelParseException.CommonErrorReadCkModel(filePath, e);
        }

        _logger.LogInformation("Validating CK model...");
        await _ckValidationService.ValidateAsync(model, operationResult);
    }
}