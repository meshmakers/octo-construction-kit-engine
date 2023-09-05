using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Interface for a serializer for the CK model
/// </summary>
public interface ICkSerializer
{
    /// <summary>
    /// Serializes the compiled model to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="compiledModel">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkCompiledModelRoot compiledModel);
    
    /// <summary>
    /// Serializes the meta data to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="metaRootDto">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkMetaRootDto metaRootDto);
    
    /// <summary>
    /// Serializes the elements to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="elementsRootDto">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, CkElementsRootDto elementsRootDto);
    
    /// <summary>
    /// Deserializes the meta data from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkMetaRootDto> DeserializeMetaAsync(Stream stream, OperationResult operationResult);
    
    /// <summary>
    /// Deserializes the elements from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkElementsRootDto> DeserializeElementsAsync(Stream stream, OperationResult operationResult);
    
    /// <summary>
    /// Deserializes the compiled model from a string.
    /// </summary>
    /// <param name="s">The text containing the construction kit to read</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(string s, OperationResult operationResult);
    
    /// <summary>
    /// Deserializes the compiled model from a string.
    /// </summary>
    /// <param name="s">The text containing the construction kit to read</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    CkCompiledModelRoot DeserializeCompiledModelRoot(string s, OperationResult operationResult);
    
    /// <summary>
    /// Deserializes the compiled model from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<CkCompiledModelRoot> DeserializeCompiledModelRootAsync(Stream stream, OperationResult operationResult);
}