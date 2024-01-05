using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
///     Maps a document to a dto and vice versa
/// </summary>
/// <typeparam name="TDto"></typeparam>
/// <typeparam name="TDocument"></typeparam>
/// <typeparam name="TKey"></typeparam>
public interface IDataSourceMapper<TKey, TDocument, TDto> where TKey : notnull
{
    /// <summary>
    ///     Returns the id of an object
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    TKey GetId(TDto dto);

    /// <summary>
    ///     Returns the id of an object
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    TKey GetId(TDocument document);

    /// <summary>
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    TDocument MapToDocument(TDto dto);

    /// <summary>
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    TDto MapToDto(TDocument document);

    /// <summary>
    ///     Apply the changes from the documentToApply to the savedDocument
    /// </summary>
    /// <param name="savedDocument">The document changes of the other document has to be applied to</param>
    /// <param name="documentToApply">The document with the pending changes</param>
    void Apply(TDocument savedDocument, TDocument documentToApply);

    /// <summary>
    ///     Serializes the model to the stream.
    /// </summary>
    /// <param name="streamWriter">A stream ready to write used for serialization</param>
    /// <param name="dictionary">Model to serialize</param>
    /// <returns></returns>
    Task SerializeAsync(StreamWriter streamWriter, IReadOnlyDictionary<TKey, TDocument> dictionary);

    /// <summary>
    ///     Deserializes the model from the stream.
    /// </summary>
    /// <param name="stream">The stream to read</param>
    /// <param name="locationReference">A reference used in messages to signal the position of a file or resource</param>
    /// <param name="operationResult">
    ///     A operation result object that lists all validation issues. In case of exceptions this object contains the
    ///     validation errors too.
    /// </param>
    /// <returns></returns>
    Task<IReadOnlyDictionary<TKey, TDocument>> DeserializeAsync(Stream stream, string locationReference, OperationResult operationResult);
}