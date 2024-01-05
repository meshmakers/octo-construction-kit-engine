using System.Text.Json.Nodes;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.Serialization.Schema;
using Meshmakers.Octo.Runtime.Engine.Messages;
using Yaml2JsonNode;
using YamlDotNet.RepresentationModel;

namespace Meshmakers.Octo.Runtime.Engine.Serialization;

/// <summary>
///     Implements a validator for the CK model in JSON or YAML format.
/// </summary>
internal class RtSchemaValidator : IRtSchemaValidator
{
    /// <inheritdoc />
    public bool ValidateModelInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, RtSchema.GetRuntimeSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateModelInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, RtSchema.GetRuntimeSchema(), locationReference, operationResult);
    }

    private static bool ValidateModelJson(Stream stream, JsonSchema schema, string locationReference, OperationResult operationResult)
    {
        var json = JsonNode.Parse(stream);

        var evaluationResults = schema.Evaluate(json, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return ValidateEvaluationResults(locationReference, operationResult, evaluationResults);
    }

    private static bool ValidateModelYaml(Stream stream, JsonSchema schema, string locationReference, OperationResult operationResult)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        stream.Position = 0;
        memoryStream.Seek(0, SeekOrigin.Begin);

        using var streamReader = new StreamReader(memoryStream);
        var yamlStream = new YamlStream();
        yamlStream.Load(streamReader);
        var singleNode = yamlStream.Documents[0].ToJsonNode();

        var evaluationResults = schema.Evaluate(singleNode, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return ValidateEvaluationResults(locationReference, operationResult, evaluationResults);
    }

    private static bool ValidateEvaluationResults(string locationReference, OperationResult operationResult,
        EvaluationResults evaluationResults)
    {
        if (!evaluationResults.IsValid)
        {
            foreach (var evaluationResult in evaluationResults.Details.Where(x => x.HasErrors))
            {
                var path = evaluationResult.InstanceLocation.ToString();
                var errorMessages = string.Join(", ", evaluationResults.Errors?.Values ?? Enumerable.Empty<string>());
                operationResult.AddMessage(MessageCodes.SchemaValidationError(locationReference, $"{path}: {errorMessages}"));
            }
        }

        return evaluationResults.IsValid;
    }
}