namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Interface of algorithms that run before the persistence of documents
/// </summary>
/// <typeparam name="TDocument"></typeparam>
public interface IPreDocumentModification<in TDocument>
{
    /// <summary>
    ///     Runs pre persistence modifications on the given documents
    /// </summary>
    /// <param name="session"></param>
    /// <param name="documents"></param>
    /// <returns></returns>
    Task RunAsync(IOctoSession session, IEnumerable<TDocument> documents);
}