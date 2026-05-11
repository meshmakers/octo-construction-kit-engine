using Json.Pointer;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
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
    public bool ValidateCompiledModelInJson(Stream stream, string locationReference, OperationResult operationResult,
        bool tolerantToUnknownProperties = false)
    {
        return ValidateModelJson(stream, CkSchema.GetCompiledModelSchema(), locationReference, operationResult, tolerantToUnknownProperties);
    }

    /// <inheritdoc />
    public bool ValidateElementsInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetElementsSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateModelConfigInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetModelConfigSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateMetaInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetMetaSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateCompiledModelInYaml(Stream stream, string locationReference, OperationResult operationResult,
        bool tolerantToUnknownProperties = false)
    {
        return ValidateModelYaml(stream, CkSchema.GetCompiledModelSchema(), locationReference, operationResult, tolerantToUnknownProperties);
    }

    /// <inheritdoc />
    public bool ValidateMigrationMetaInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetMigrationMetaSchema(), locationReference, operationResult);
    }

    /// <inheritdoc />
    public bool ValidateMigrationScriptInYaml(Stream stream, string locationReference, OperationResult operationResult)
    {
        return ValidateModelYaml(stream, CkSchema.GetMigrationScriptSchema(), locationReference, operationResult);
    }

    private static bool ValidateModelJson(Stream stream, JsonSchema schema, string locationReference, OperationResult operationResult,
        bool tolerantToUnknownProperties = false)
    {
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        var jsonElement = document.RootElement;

        var evaluationResults = schema.Evaluate(jsonElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        return ValidateEvaluationResults(locationReference, operationResult, evaluationResults, jsonElement, tolerantToUnknownProperties);
    }

    private static bool ValidateModelYaml(Stream stream, JsonSchema schema, string locationReference, OperationResult operationResult,
        bool tolerantToUnknownProperties = false)
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
        return ValidateEvaluationResults(locationReference, operationResult, evaluationResults, jsonElement, tolerantToUnknownProperties);
    }

    private static bool ValidateEvaluationResults(string locationReference, OperationResult operationResult,
        EvaluationResults evaluationResults, System.Text.Json.JsonElement? rootElement = null,
        bool tolerantToUnknownProperties = false)
    {
        if (evaluationResults.IsValid)
        {
            return true;
        }

        var details = evaluationResults.Details;
        if (details == null)
        {
            return false;
        }

        var failingDetails = details.Where(x => x.Errors != null && x.Errors.Count > 0).ToList();

        if (tolerantToUnknownProperties)
        {
            // additionalProperties violations show up in the EvaluationPath of the failing detail
            // (e.g. "/properties/types/items/$ref/.../additionalProperties"). Strip them out and
            // re-check whether anything else still fails.
            failingDetails = failingDetails
                .Where(d => !d.EvaluationPath.ToString().Contains("/additionalProperties"))
                .ToList();
            if (failingDetails.Count == 0)
            {
                return true;
            }
        }

        foreach (var evaluationResult in failingDetails)
        {
            var path = evaluationResult.InstanceLocation.ToString();
            var errorMessages = evaluationResult.Errors != null
                ? string.Join(", ", evaluationResult.Errors.Values)
                : string.Empty;

            // Get detailed information about the failing object
            var objectDetails = GetObjectDetails(rootElement, evaluationResult.InstanceLocation);
            var enhancedErrorMessage = string.IsNullOrEmpty(objectDetails) ? errorMessages : $"{errorMessages}. Object details: {objectDetails}";

            operationResult.AddMessage(MessageCodes.SchemaValidationError(locationReference, path, enhancedErrorMessage));
        }

        return false;
    }

    private const int MaxDisplayedProperties = 10;
    private const int MaxDisplayedArrayElements = 5;

    private static string GetObjectDetails(System.Text.Json.JsonElement? rootElement, JsonPointer instanceLocation)
    {
        if (rootElement == null)
        {
            return string.Empty;
        }

        try
        {
            var targetElement = instanceLocation.Evaluate(rootElement.Value);

            if (!targetElement.HasValue)
            {
                return "Target object not found";
            }

            var element = targetElement.Value;
            return element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Object => FormatObjectProperties(element),
                System.Text.Json.JsonValueKind.Array => FormatArrayElements(element),
                _ => $"Value: {FormatElementValue(element)}"
            };
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string FormatObjectProperties(System.Text.Json.JsonElement element)
    {
        var properties = element.EnumerateObject()
            .Take(MaxDisplayedProperties)
            .Select(property => $"{property.Name}: {FormatElementValue(property.Value)}")
            .ToList();

        var totalCount = element.EnumerateObject().Count();
        var additionalPropertiesInfo = totalCount > MaxDisplayedProperties 
            ? $", ... and {totalCount - MaxDisplayedProperties} more properties" 
            : string.Empty;

        return $"{{ {string.Join(", ", properties)}{additionalPropertiesInfo} }}";
    }

    private static string FormatArrayElements(System.Text.Json.JsonElement element)
    {
        var elements = element.EnumerateArray()
            .Take(MaxDisplayedArrayElements)
            .Select(FormatElementValue);

        var totalCount = element.GetArrayLength();
        var additionalElementsInfo = totalCount > MaxDisplayedArrayElements ? "..." : string.Empty;

        return $"Array with {totalCount} elements: [{string.Join(", ", elements)}{additionalElementsInfo}]";
    }

    private static string FormatElementValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null => "null",
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            System.Text.Json.JsonValueKind.Number => element.GetRawText(),
            System.Text.Json.JsonValueKind.String => $"\"{element.GetString()}\"",
            System.Text.Json.JsonValueKind.Object => $"{{object with {element.EnumerateObject().Count()} properties}}",
            System.Text.Json.JsonValueKind.Array => $"[array with {element.GetArrayLength()} elements]",
            _ => element.ValueKind.ToString()
        };
    }
}