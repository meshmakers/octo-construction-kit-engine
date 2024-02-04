using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

/// <summary>
///     Implements a serializer for the CK model in JSON format.
/// </summary>
internal class CkJsonSerializer : ICkJsonSerializer
{
    private const string Validation = "validation";
    private readonly JsonSerializerOptions _options;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    /// <summary>
    ///     Creates a new instance of the <see cref="CkJsonSerializer" /> class.
    /// </summary>
    public CkJsonSerializer()
    {
        _options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters =
            {
                new OctoValidatingJsonConverterFactory { RequireFormatValidation = true, OutputFormat = OutputFormat.List }
            }
        };
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, CkCompiledModelRoot compiledModel)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, compiledModel, _options).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, CkMetaRootDto metaRootDto)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, metaRootDto, _options).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, CkElementsRootDto elementsRootDto)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, elementsRootDto, _options).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CkMetaRootDto> DeserializeMetaAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        try
        {
            var ckMetaDto = await JsonSerializer.DeserializeAsync<CkMetaRootDto>(stream, _options).ConfigureAwait(false);
            return ckMetaDto ?? throw ModelParseException.CannotDeserializeModel(operationResult);
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw ModelParseException.CannotDeserializeModel(operationResult);
        }
    }

    /// <inheritdoc />
    public async Task<CkElementsRootDto> DeserializeElementsAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        try
        {
            var ckElementsDto = await JsonSerializer.DeserializeAsync<CkElementsRootDto>(stream, _options).ConfigureAwait(false);
            return ckElementsDto ?? throw ModelParseException.CannotDeserializeModel(operationResult);
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw ModelParseException.CannotDeserializeModel(operationResult);
        }
    }


    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, string locationReference,
        OperationResult operationResult)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return await DeserializeCompiledModelRootAsync(memStream, locationReference, operationResult).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public CkCompiledModelRoot DeserializeCompiledModelRoot(string s, string locationReference, OperationResult operationResult)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return DeserializeCompiledModelRoot(memStream, locationReference, operationResult);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, string locationReference,
        OperationResult operationResult)
    {
        try
        {
            var ckModelRoot = await JsonSerializer.DeserializeAsync<CkCompiledModelRoot>(stream, _options).ConfigureAwait(false);
            return ckModelRoot ?? throw ModelParseException.CannotDeserializeModel(operationResult);
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw ModelParseException.CannotDeserializeModel(operationResult);
        }
    }

    private CkCompiledModelRoot DeserializeCompiledModelRoot(Stream stream, string locationReference, OperationResult operationResult)
    {
        try
        {
            var ckModelRoot = JsonSerializer.Deserialize<CkCompiledModelRoot>(stream, _options);
            return ckModelRoot ?? throw ModelParseException.CannotDeserializeModel(operationResult);
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw ModelParseException.CannotDeserializeModel(operationResult);
        }
    }


    private static void CheckException(string locationReference, OperationResult operationResult, JsonException e)
    {
        if (e.Data.Contains(Validation))
        {
            var evaluationResults = (EvaluationResults?)e.Data[Validation];
            if (evaluationResults != null)
            {
                if (!ValidateEvaluationResults(locationReference, operationResult, evaluationResults))
                {
                    throw ModelParseException.SchemaValidationFailed(locationReference, operationResult);
                }
            }
        }
    }

    private static bool ValidateEvaluationResults(string locationReference, OperationResult operationResult,
        EvaluationResults evaluationResults)
    {
        if (!evaluationResults.IsValid)
        {
            foreach (var evaluationResult in evaluationResults.Details.Where(x => x.HasErrors))
            {
                var path = evaluationResult.InstanceLocation.ToString();
                var errorMessages = string.Join(", ", evaluationResult.Errors?.Values ?? Enumerable.Empty<string>());
                operationResult.AddMessage(MessageCodes.SchemaValidationError(locationReference, path, errorMessages));
            }
        }

        return evaluationResults.IsValid;
    }
}