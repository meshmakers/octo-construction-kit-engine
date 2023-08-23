using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Validation;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Serialization;

public class CkModelReader
{
    private readonly ICkModelValidator _ckModelValidator;
    private readonly ILogger<CkModelReader> _logger;
    private readonly ICkSerializer _ckSerializer;

    public CkModelReader(ILogger<CkModelReader> logger, ICkSerializer ckSerializer, ICkModelValidator ckModelValidator)
    {
        _ckModelValidator = ckModelValidator;
        _logger = logger;
        _ckSerializer = ckSerializer;
    }

    public async Task ReadAsync(string filePath, OperationResult operationResult, CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Reading CK model...");

        CkCompiledModelRoot? model;

        try
        {
            await using var stream = File.OpenRead(filePath);
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
        await _ckModelValidator.ValidateAsync(model, operationResult);

        
    }
}