namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Interface for schema validation of construction kit models
/// </summary>
public interface ICkSchemaValidator
{
    /// <summary>
    ///     Validates the elements in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateElementsInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the meta data in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateMetaInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the compiled model in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateCompiledModelInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the elements in the stream using YAML format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in YAML format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateElementsInYaml(Stream stream, string locationReference, OperationResult operationResult);
    
    /// <summary>
    ///     Validates the construction kit model configuration in the stream using YAML format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in YAML format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateModelConfigInYaml(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the meta data in the stream using YAML format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in YAML format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateMetaInYaml(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the compiled model in the stream using YAML format.
    /// </summary>
    /// <param name="stream">Stream containing construction kit model in YAML format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateCompiledModelInYaml(Stream stream, string locationReference, OperationResult operationResult);
}