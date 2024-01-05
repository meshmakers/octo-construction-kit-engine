using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     Represents a json serializer for runtime repository
/// </summary>
public interface IRtRepositorySerializer
{
    /// <summary>
    ///     Serializes a list of entities to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="collection">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, IEnumerable<RtEntity> collection);

    /// <summary>
    ///     Serializes a list of associations to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="collection">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, IEnumerable<RtAssociation> collection);

    /// <summary>
    ///     Deserializes the runtime model entities from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns></returns>
    Task<IEnumerable<RtEntity>> DeserializeEntitiesAsync(Stream stream, string locationReference,
        OperationResult operationResult);

    /// <summary>
    ///     Deserializes the runtime model associations from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns></returns>
    Task<IEnumerable<RtAssociation>> DeserializeAssociationsAsync(Stream stream, string locationReference, OperationResult operationResult);
}