using System.Text.Json.Nodes;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization.Schema;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using YamlDotNet.RepresentationModel;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     Implements a validator for blueprint models in JSON or YAML format.
/// </summary>
public class BlueprintSchemaValidator : IBlueprintSchemaValidator
{
    /// <inheritdoc />
    public bool ValidateMetaInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, BlueprintSchema.GetMetaSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateMetaInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, BlueprintSchema.GetMetaSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateCatalogIndexInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, BlueprintSchema.GetCatalogIndexSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateLibraryVersionsInJson(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelJson(stream, BlueprintSchema.GetLibraryVersionsSchema(), locationReference, operationResult);
    }

    private static bool ValidateModelJson(Stream stream, JsonSchema schema, string locationReference, OperationResult operationResult)
    {
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        var jsonElement = document.RootElement;

        var evaluationResults = schema.Evaluate(jsonElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
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
        if (yamlStream.Documents.Count == 0)
        {
            operationResult.AddMessage(MessageCodes.FileContainsNoModel(locationReference));
            return true;
        }
        var singleNode = yamlStream.Documents[0].ToJsonNode();

        // Convert JsonNode to JsonElement for JsonSchema.Net 8.0
        var jsonString = singleNode?.ToJsonString() ?? "null";
        using var document = System.Text.Json.JsonDocument.Parse(jsonString);
        var jsonElement = document.RootElement;

        var evaluationResults = schema.Evaluate(jsonElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return ValidateEvaluationResults(locationReference, operationResult, evaluationResults);
    }

    private static bool ValidateEvaluationResults(string locationReference, OperationResult operationResult,
        EvaluationResults evaluationResults)
    {
        if (!evaluationResults.IsValid)
        {
            // In JsonSchema.Net 8.0, HasErrors was removed. Check for errors using Errors property.
            // Details may be null, and Errors is now Dictionary<string, string> (not nullable)
            var details = evaluationResults.Details;
            if (details != null)
            {
                foreach (var evaluationResult in details.Where(x => x.Errors != null && x.Errors.Count > 0))
                {
                    var path = evaluationResult.InstanceLocation.ToString();
                    var errorMessages = evaluationResult.Errors != null
                        ? string.Join(", ", evaluationResult.Errors.Values)
                        : string.Empty;
                    operationResult.AddMessage(MessageCodes.SchemaValidationError(locationReference, path, errorMessages));
                }
            }
        }

        return evaluationResults.IsValid;
    }
}
