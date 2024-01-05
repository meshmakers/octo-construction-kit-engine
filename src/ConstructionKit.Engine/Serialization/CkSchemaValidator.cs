using System.Text.Json.Nodes;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Yaml2JsonNode;
using YamlDotNet.RepresentationModel;

namespace Meshmakers.Octo.ConstructionKit.Engine.Serialization;

/// <summary>
///     Implements a validator for the CK model in JSON or YAML format.
/// </summary>
internal class CkSchemaValidator : ICkSchemaValidator
{
    /// <inheritdoc />
    public bool ValidateElementsInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, CkSchema.GetElementsSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateMetaInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, CkSchema.GetMetaSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateCompiledModelInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, CkSchema.GetCompiledModelSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateElementsInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetElementsSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateMetaInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetMetaSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateCompiledModelInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetCompiledModelSchema(), locationReference, operationResult);
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
                var errorMessages = string.Join(", ", evaluationResult.Errors?.Values ?? Enumerable.Empty<string>());
                operationResult.AddMessage(MessageCodes.SchemaValidationError(locationReference, $"{path}: {errorMessages}"));
            }
        }

        return evaluationResults.IsValid;
    }
}