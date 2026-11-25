namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Checks partial updates before anything of a bulk mutation is written (AB#9755). The
///     <see cref="IPreDocumentModification{TDocument}" /> pipeline runs on insert and replace only, so a rule that a
///     stored entity must keep (for example the configuration of a number range) is checked here for updates: the
///     validation gets the partial documents (only the attributes the update sets, with <c>RtId</c> and
///     <c>CkTypeId</c>), reads what it needs itself and throws to refuse the whole batch. Atomic record-array item
///     updates (<c>RecordArrayItemUpdates</c>) are not part of the partial documents; a validation of a record-array
///     attribute does not see them.
/// </summary>
/// <typeparam name="TDocument">The document type</typeparam>
public interface IPreDocumentUpdateValidation<in TDocument>
{
    /// <summary>
    ///     Validates the partial updates of one bulk mutation; throws to refuse them.
    /// </summary>
    /// <param name="session">Session (and transaction) of the write</param>
    /// <param name="repositoryDataSource">Repository data source to read the stored documents from</param>
    /// <param name="partialDocuments">The partial documents of the update</param>
    Task RunAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<TDocument> partialDocuments);
}
