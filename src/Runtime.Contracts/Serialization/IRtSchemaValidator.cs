using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     Interface for schema validation of runtime models
/// </summary>
public interface IRtSchemaValidator
{
    /// <summary>
    ///     Validates the runtime model in the stream using JSON format.
    /// </summary>
    /// <param name="stream">Stream containing runtime model in JSON format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateModelInJson(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Validates the runtime model in the stream using YAML format.
    /// </summary>
    /// <param name="stream">Stream containing runtime model in YAML format.</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">The result object that contains after call validation messages.</param>
    /// <returns></returns>
    bool ValidateModelInYaml(Stream stream, string locationReference, OperationResult operationResult);
}