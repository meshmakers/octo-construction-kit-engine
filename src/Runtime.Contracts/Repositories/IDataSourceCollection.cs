namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Interface for the management of a collection of entities in a data source
/// </summary>
public interface IDataSourceCollection<in TKey, TDocument> where TDocument : new() where TKey : notnull
{
    /// <summary>
    /// Inserts a new document into the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="document">The document to insert</param>
    /// <returns></returns>
    Task InsertOneAsync(IOctoSession session, TDocument document);
    
    /// <summary>
    /// Inserts multiple documents into the collection
    /// </summary>
    /// <param name="session"></param>
    /// <param name="documents"></param>
    /// <returns></returns>
    Task InsertManyAsync(IOctoSession session, IEnumerable<TDocument> documents);
    
    /// <summary>
    /// Updates multiple documents in the collection
    /// </summary>
    /// <remarks>
    /// Attention! This method updates existing attributes of a document. Not mentioned (or null) attributes are not updated.
    /// </remarks>
    /// <param name="session">The session object</param>
    /// <param name="documents">A list of documents to update</param>
    /// <returns></returns>
    Task UpdateManyAsync(IOctoSession session, IEnumerable<TDocument> documents);

    /// <summary>
    /// Replaces multiple documents in the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="documents">A list of documents to replaced, based on the runtime object id</param>
    /// <returns></returns>
    Task ReplaceManyAsync(IOctoSession session, IEnumerable<TDocument> documents);

    /// <summary>
    /// Replace a document in the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <param name="document">The document the existing of is replaced</param>
    /// <returns></returns>
    Task ReplaceByIdAsync(IOctoSession session, TKey key, TDocument document);

    /// <summary>
    /// Deletes the document with the given key
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <returns></returns>
    Task DeleteOneAsync(IOctoSession session, TKey key);
    
    /// <summary>
    /// Deletes documents with the given id
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="keys">A list of unique keys of the documents</param>
    /// <returns></returns>
    Task DeleteManyAsync(IOctoSession session, IEnumerable<TKey> keys);

    /// <summary>
    /// Gets the document with the given key
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <returns></returns>
    Task<TDocument?> DocumentAsync(IOctoSession session, TKey key);

    /// <summary>
    /// Gets a queryable interface of the given type to the data source
    /// </summary>
    /// <returns></returns>
    Task<IQueryable<TDocument>> AsQueryableAsync();

}