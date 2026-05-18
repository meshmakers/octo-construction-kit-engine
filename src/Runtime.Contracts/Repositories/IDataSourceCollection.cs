using System.Linq.Expressions;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Interface for the management of a collection of entities in a data source
/// </summary>
public interface IDataSourceCollection<in TKey, TDocument> where TDocument : new() where TKey : notnull
{
    /// <summary>
    ///     Imports a list of documents into the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="documents">The list of documents to import</param>
    /// <param name="options">Options for the bulk operation</param>
    /// <returns>Returns the result of the import</returns>
    Task<IBulkImportResult> BulkImportAsync(IOctoSession session, IEnumerable<TDocument> documents,
        BulkOperationOptions options);

    /// <summary>
    ///     Inserts a new document into the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="document">The document to insert</param>
    /// <returns></returns>
    Task InsertOneAsync(IOctoSession session, TDocument document);

    /// <summary>
    ///     Inserts multiple documents into the collection
    /// </summary>
    /// <param name="session"></param>
    /// <param name="documents"></param>
    /// <returns></returns>
    Task InsertManyAsync(IOctoSession session, IEnumerable<TDocument> documents);

    /// <summary>
    ///     Updates multiple documents in the collection
    /// </summary>
    /// <remarks>
    ///     Attention! This method updates existing attributes of a document. Not mentioned (or null) attributes are not updated.
    /// </remarks>
    /// <param name="session">The session object</param>
    /// <param name="documents">A list of documents to update</param>
    /// <returns></returns>
    Task UpdateOneAsync(IOctoSession session, IEnumerable<TDocument> documents);

    /// <summary>
    ///     Updates a single document if the optimistic-concurrency guard matches the
    ///     persisted state. The write is silently skipped if the guard does not match,
    ///     so a stale write cannot overwrite a more-recent one.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="document">Document with the new values; the id is taken from this document</param>
    /// <param name="guard">Guard expression that must be satisfied for the write to apply</param>
    /// <returns>
    ///     <c>true</c> if the document was updated; <c>false</c> if the guard did not
    ///     match — the document was either absent or had a value at the guard's
    ///     <see cref="AttributeNewerThanGuard.AttributePath" /> greater than the guard's
    ///     <see cref="AttributeNewerThanGuard.NewValue" />.
    /// </returns>
    Task<bool> UpdateOneIfGuardMatchesAsync(IOctoSession session, TDocument document, AttributeNewerThanGuard guard);

    /// <summary>
    /// Updates multiple documents in the collection based on a filter expression
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="expression">Filter expression to filter for documents that need to be updated</param>
    /// <param name="document">Document template with new values</param>
    /// <returns></returns>
    Task UpdateManyAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression,
        TDocument document);

    /// <summary>
    ///     Replaces multiple documents in the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="documents">A list of documents to replaced, based on the runtime object id</param>
    /// <returns></returns>
    Task ReplaceManyAsync(IOctoSession session, IEnumerable<TDocument> documents);

    /// <summary>
    ///     Replace a document in the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <param name="document">The document the existing of is replaced</param>
    /// <returns></returns>
    Task ReplaceByIdAsync(IOctoSession session, TKey key, TDocument document);

    /// <summary>
    ///     Deletes the document with the given key
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <returns></returns>
    Task DeleteOneAsync(IOctoSession session, TKey key);

    /// <summary>
    ///     Deletes the document with the given key without exceptions and with return value that indicates if the document has been deleted
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <returns>True, when the document has been deleted, otherwise false</returns>
    Task<bool> TryDeleteOneAsync(IOctoSession session, TKey key);

    /// <summary>
    /// Deletes the first document that matches the given expression without exceptions and with return value that indicates if the document has been deleted
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="expression">Filter expression</param>
    /// <returns>True, when the document has been deleted, otherwise false</returns>
    Task<bool> TryDeleteOneAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression);

    /// <summary>
    ///     Deletes documents with the given id
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="keys">A list of unique keys of the documents</param>
    /// <returns></returns>
    Task DeleteOneAsync(IOctoSession session, IEnumerable<TKey> keys);

    /// <summary>
    /// Deletes all documents matching the given expression
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="expression">Filter expression</param>
    /// <returns></returns>
    Task DeleteManyAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression);

    /// <summary>
    ///     Gets the document with the given key
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <returns>The document or null if not found</returns>
    Task<TDocument?> DocumentAsync(IOctoSession session, TKey key);

    /// <summary>
    ///     Gets the document with the given key
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="keys">A list of unique keys for the documents</param>
    /// <returns>A list of documents</returns>
    Task<IReadOnlyList<TDocument>> DocumentsAsync(IOctoSession session, IEnumerable<TKey> keys);

    /// <summary>
    ///     Gets the document with the given key
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="key">The unique key</param>
    /// <typeparam name="TDerived">The type of the derived document</typeparam>
    /// <returns></returns>
    Task<TDerived?> DocumentAsync<TDerived>(IOctoSession session, TKey key)
        where TDerived : TDocument, new();

    /// <summary>
    ///     Gets a queryable interface of the given type to the data source
    /// </summary>
    /// <param name="session">The session object</param>
    /// <returns></returns>
    Task<IQueryable<TDocument>> AsQueryableAsync(IOctoSession? session = null);

    /// <summary>
    ///     Gets a queryable interface of the given type to the data source
    /// </summary>
    /// <param name="session">The session object</param>
    /// <returns></returns>
    IQueryable<TDocument> AsQueryable(IOctoSession? session = null);

    /// <summary>
    ///     Finds a document by the given expression
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="expression">Filter expression</param>
    /// <returns>The document</returns>
    Task<TDocument?> FindSingleOrDefaultAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression);

    /// <summary>
    ///     Finds a document by the given expression
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="expression">Filter expression</param>
    /// <param name="skip">Amount of documents to skip</param>
    /// <param name="take">Maximum amount of documents to return</param>
    /// <returns>List of documents</returns>
    Task<ICollection<TDocument>> FindManyAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression,
        int? skip = null, int? take = null);

    /// <summary>
    ///     Gets the total count of documents in the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <returns>Total count</returns>
    Task<long> GetTotalCountAsync(IOctoSession session);

    /// <summary>
    ///     Gets the total count of documents in the collection that match the given filter
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="expression">Filter expression</param>
    /// <returns>Total count</returns>
    Task<long> GetTotalCountAsync(IOctoSession session, Expression<Func<TDocument, bool>> expression);

    /// <summary>
    ///     Gets all documents of the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="skip">Amount of documents to skip</param>
    /// <param name="take">Maximum amount of documents to return</param>
    /// <returns>List of documents</returns>
    Task<IEnumerable<TDocument>> GetAsync(IOctoSession session, int? skip = null, int? take = null);
}