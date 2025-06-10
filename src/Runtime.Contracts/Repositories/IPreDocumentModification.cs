namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Interface of algorithms that run before the persistence of documents
/// </summary>
/// <typeparam name="TDocument"></typeparam>
public interface IPreDocumentModification<in TDocument>
{
    /// <summary>
    ///     Runs pre-persistence modifications on the given documents
    /// </summary>
    /// <param name="session">Session to use for the operation</param>
    /// <param name="repositoryDataSource">Repository data source to load or update additional data</param>
    /// <param name="documents">The documents to modify</param>
    /// <returns></returns>
    Task RunAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, IEnumerable<TDocument> documents);
}