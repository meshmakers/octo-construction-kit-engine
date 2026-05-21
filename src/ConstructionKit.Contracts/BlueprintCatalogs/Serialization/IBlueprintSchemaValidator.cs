namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

/// <summary>
///     Interface for schema validation of blueprint models
/// </summary>
public interface IBlueprintSchemaValidator
{
    /// <summary>
    ///     Validates the blueprint metadata in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing blueprint metadata in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateMetaInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the blueprint metadata in the stream using YAML format.
    /// </summary>
    /// <param name="stream">Stream containing blueprint metadata in YAML format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateMetaInYaml(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the blueprint catalog index in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing catalog index in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateCatalogIndexInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the blueprint library versions in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing library versions in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateLibraryVersionsInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the embedded-blueprint cache (produced by the BlueprintEmbed MSBuild task and
    ///     consumed by the BlueprintSourceGenerator) using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing the cache JSON.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource.</param>
    /// <param name="operationResult">The result object that contains after-call validation messages.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateCacheInJson(Stream stream, string locationReference, OperationResult operationResult);
}
