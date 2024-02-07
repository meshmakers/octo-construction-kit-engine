using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Messages;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
///     Implements a serializer for the runtime in JSON format.
/// </summary>
internal class RtJsonSerializer : IRtJsonSerializer
{
    private const string Validation = "validation";
    private readonly JsonSerializerOptions _options;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    /// <summary>
    ///     Creates a new instance of the <see cref="RtJsonSerializer" /> class.
    /// </summary>
    public RtJsonSerializer()
    {
        _options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new OctoValidatingJsonConverterFactory { RequireFormatValidation = true, OutputFormat = OutputFormat.List }
            }
        };
    }

    /// <inheritdoc />
    public async Task SerializeAsync(StreamWriter streamWriter, RtModelRootDto modelRootDto)
    {
        await JsonSerializer.SerializeAsync(streamWriter.BaseStream, modelRootDto, _options).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IRtDeserializeStream> DeserializeStreamAsync(Stream stream, CancellationToken? cancellationToken = null)
    {
        RtDeserializeStream deserializeStream = new(stream, 5000);

        await deserializeStream.InitializeAsync(cancellationToken).ConfigureAwait(false);

        return deserializeStream;
    }

    /// <inheritdoc />
    public async Task<RtModelRootDto> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult)
    {
        try
        {
            var ckMetaDto = await JsonSerializer.DeserializeAsync<RtModelRootDto>(stream, _options).ConfigureAwait(false);
            return ckMetaDto ?? throw RuntimeModelParseException.CannotDeserializeModel(operationResult);
        }
        catch (JsonException e)
        {
            CheckException(locationReference, operationResult, e);
            throw RuntimeModelParseException.CannotDeserializeModel(operationResult);
        }
    }

    /// <inheritdoc />
    public async Task<RtModelRootDto> DeserializeAsync(string s, string locationReference, OperationResult operationResult)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        using var memStream = new MemoryStream(byteArray);
        return await DeserializeAsync(memStream, locationReference, operationResult).ConfigureAwait(false);
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
                    throw RuntimeModelParseException.SchemaValidationFailed(locationReference, operationResult);
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