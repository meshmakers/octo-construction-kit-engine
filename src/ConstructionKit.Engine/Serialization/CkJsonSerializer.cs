using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

/// <summary>
/// Implements a serializer for the CK model in JSON format.
/// </summary>
public class CkJsonSerializer : ICkJsonSerializer
{
    private const string Validation = "validation";
    private readonly JsonSerializerOptions _options;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    /// <summary>
    /// Creates a new instance of the <see cref="CkJsonSerializer"/> class.
    /// </summary>
    public CkJsonSerializer()
    {
        _options = new JsonSerializerOptions 
        { 
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters =
            {
                new OctoValidatingJsonConverterFactory {RequireFormatValidation = true, OutputFormat = OutputFormat.List}
            }
        };
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, CkCompiledModelRoot compiledModel)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, compiledModel, _options);
    }
    
    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, CkMetaRootDto metaRootDto)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, metaRootDto, _options);
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, CkElementsRootDto elementsRootDto)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, elementsRootDto, _options);
    }

    /// <inheritdoc />
    public async Task<CkMetaRootDto> DeserializeMetaAsync(Stream stream, OperationResult operationResult)
    {
        try
        {
            var ckMetaDto = await JsonSerializer.DeserializeAsync<CkMetaRootDto>(stream, _options);
            return ckMetaDto ?? throw ModelParseException.CannotDeserializeModel();
        }
        catch (JsonException e)
        {
            CheckException(operationResult, e);
            throw ModelParseException.CannotDeserializeModel();
        }
    }

    /// <inheritdoc />
    public async Task<CkElementsRootDto> DeserializeElementsAsync(Stream stream, OperationResult operationResult)
    {
        try
        {
            var ckElementsDto = await JsonSerializer.DeserializeAsync<CkElementsRootDto>(stream, _options);
            return ckElementsDto ?? throw ModelParseException.CannotDeserializeModel();
        }
        catch (JsonException e)
        {
            CheckException(operationResult, e);
            throw ModelParseException.CannotDeserializeModel();
        }
    }


    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, OperationResult operationResult) 
    {
        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return await DeserializeCompiledModelRootAsync(memStream, operationResult);
    }

    /// <inheritdoc />
    public CkCompiledModelRoot DeserializeCompiledModelRoot(string s, OperationResult operationResult)
    {
        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return DeserializeCompiledModelRoot(memStream, operationResult);
    }

    private CkCompiledModelRoot DeserializeCompiledModelRoot(Stream stream, OperationResult operationResult)
    {
        try
        {
            var ckModelRoot = JsonSerializer.Deserialize<CkCompiledModelRoot>(stream, _options);
            return ckModelRoot ?? throw ModelParseException.CannotDeserializeModel();
        }
        catch (JsonException e)
        {
            CheckException(operationResult, e);
            throw ModelParseException.CannotDeserializeModel();
        }
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, OperationResult operationResult)
    {
        try
        {
            var ckModelRoot = await JsonSerializer.DeserializeAsync<CkCompiledModelRoot>(stream, _options);
            return ckModelRoot ?? throw ModelParseException.CannotDeserializeModel();
        }
        catch (JsonException e)
        {
            CheckException(operationResult, e);
            throw ModelParseException.CannotDeserializeModel();
        }
    }
    
    
    private static void CheckException(OperationResult operationResult, JsonException e)
    {
        if (e.Data.Contains(Validation))
        {
            var evaluationResults = (EvaluationResults?)e.Data[Validation];
            if (evaluationResults != null)
            {
                if (!ValidateEvaluationResults(operationResult, evaluationResults))
                {
                    throw ModelParseException.SchemaValidationFailed();
                }
            }
        }
    }
    
    private static bool ValidateEvaluationResults(OperationResult operationResult, EvaluationResults evaluationResults)
    {
        if (!evaluationResults.IsValid)
        {
            foreach (var evaluationResult in evaluationResults.Details.Where(x => x.HasErrors))
            {
                var path = evaluationResult.InstanceLocation.ToString();
                var errorMessages = string.Join(", ", evaluationResults.Errors?.Values ?? Enumerable.Empty<string>());
                operationResult.AddMessage(MessageCodes.SchemaValidationError($"{path}: {errorMessages}"));
            }
        }

        return evaluationResults.IsValid;
    }
}
