using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// Interface for a serializer for the runtime model
/// </summary>
public interface IRtSerializer
{
    /// <summary>
    /// Serializes the model to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="modelRootDto">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, RtModelRootDto modelRootDto);

    /// <summary>
    /// Deserializes the runtime model from the stream, optimized for huge files.
    /// </summary>
    /// <remarks>
    /// This method bypasses the schema validation because of huge file size and currently there is no way to validate the JSON schema in a streaming way (without commercial libraries)
    /// </remarks>
    /// <param name="stream">The stream to read</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<IRtDeserializeStream> DeserializeStreamAsync(Stream stream, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Deserializes the runtime model from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns></returns>
    Task<RtModelRootDto> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    /// Deserializes the runtime model from a string.
    /// </summary>
    /// <param name="s">The text containing the runtime model to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">A operation result object that lists all validation issues. In case of exceptions this object contains the validation errors too.</param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<RtModelRootDto> DeserializeAsync(string s, string locationReference, OperationResult operationResult);
}