using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

/// <summary>
/// Interface for serializing blueprint definitions
/// </summary>
public interface IBlueprintSerializer
{
    /// <summary>
    /// Serializes the blueprint metadata to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="blueprintMetaRoot">Blueprint metadata to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, BlueprintMetaRootDto blueprintMetaRoot);

    /// <summary>
    /// Deserializes the blueprint metadata from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    Task<BlueprintMetaRootDto> DeserializeBlueprintMetaAsync(Stream stream, string locationReference, OperationResult operationResult);

    /// <summary>
    /// Deserializes the blueprint metadata from a string.
    /// </summary>
    /// <param name="content">The string content to deserialize</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues.
    /// </param>
    /// <returns>The deserialized object. Please check the for validation issues in operationResult.</returns>
    BlueprintMetaRootDto DeserializeBlueprintMeta(string content, string locationReference, OperationResult operationResult);
}
