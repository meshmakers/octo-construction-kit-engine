using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Interface for a serializer for the CK model
/// </summary>
public interface ICkSerializer
{
    /// <summary>
    ///     Serializes the model configuration to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="modelConfig">Model configuration to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkModelConfigDto modelConfig);
    
    /// <summary>
    ///     Serializes the compiled model to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="compiledModel">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkCompiledModelRoot compiledModel);

    /// <summary>
    ///     Serializes the metadata to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="metaRootDto">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkMetaRootDto metaRootDto);

    /// <summary>
    ///     Serializes the elements to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="elementsRootDto">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkElementsRootDto elementsRootDto);

    /// <summary>
    ///     Deserializes the model configuration from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkModelConfigDto> DeserializeModelConfigAsync(Stream stream, string locationReference, OperationResult operationResult);
    
    /// <summary>
    ///     Deserializes the meta data from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkMetaRootDto> DeserializeMetaAsync(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Deserializes the elements from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkElementsRootDto> DeserializeElementsAsync(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Deserializes the compiled model from a string.
    /// </summary>
    /// <param name="s">The text containing the construction kit to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Deserializes the compiled model from a string with optional tolerance for unknown properties.
    /// </summary>
    /// <param name="s">The text containing the construction kit to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <param name="tolerantToUnknownProperties">
    ///     When true, properties that violate the schema's `additionalProperties: false` constraint are silently
    ///     dropped instead of failing the read. Other schema violations still fail. Use for forward-compatible reads
    ///     from persisted catalogs after schema evolution removed properties; never use on the publish/compile path.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, string locationReference, OperationResult operationResult, bool tolerantToUnknownProperties);

    /// <summary>
    ///     Deserializes the compiled model from a string.
    /// </summary>
    /// <param name="s">The text containing the construction kit to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    CkCompiledModelRoot DeserializeCompiledModelRoot(string s, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Deserializes the compiled model from a string with optional tolerance for unknown properties.
    /// </summary>
    /// <param name="s">The text containing the construction kit to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <param name="tolerantToUnknownProperties">See <see cref="DeserializeCompiledModelRootAsync(string,string,OperationResult,bool)"/>.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    CkCompiledModelRoot DeserializeCompiledModelRoot(string s, string locationReference, OperationResult operationResult, bool tolerantToUnknownProperties);

    /// <summary>
    ///     Deserializes the compiled model from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    ///     Deserializes the compiled model from the stream with optional tolerance for unknown properties.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <param name="tolerantToUnknownProperties">See <see cref="DeserializeCompiledModelRootAsync(string,string,OperationResult,bool)"/>.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, string locationReference, OperationResult operationResult, bool tolerantToUnknownProperties);
}