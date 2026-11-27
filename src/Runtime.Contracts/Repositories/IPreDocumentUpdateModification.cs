namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Modifies partial updates before they are written. The <see cref="IPreDocumentModification{TDocument}" />
///     pipeline runs on insert and replace only; a modification that must react to an update - numbering a document when
///     an update moves it into a released state - runs here. It gets the partial documents of one bulk mutation (only
///     the attributes the update sets, with <c>RtId</c> and <c>CkTypeId</c>), reads what it needs itself (in the session,
///     and so the transaction, of the write) and may set further attributes on them: they are written in the same
///     statement as the update. It runs after the <see cref="IPreDocumentUpdateValidation{TDocument}" /> hooks and is
///     skipped like the other modifications when they are disabled (restore, raw import). Atomic record-array item
///     updates (<c>RecordArrayItemUpdates</c>) are not part of the partial documents.
/// </summary>
/// <typeparam name="TDocument">The document type</typeparam>
public interface IPreDocumentUpdateModification<in TDocument>
{
    /// <summary>
    ///     Modifies the partial updates of one bulk mutation before they are written.
    /// </summary>
    /// <param name="session">Session (and transaction) of the write</param>
    /// <param name="repositoryDataSource">Repository data source to read the stored documents from</param>
    /// <param name="partialDocuments">The partial documents of the update; attributes set on them are written too</param>
    Task ModifyUpdatesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<TDocument> partialDocuments);
}
