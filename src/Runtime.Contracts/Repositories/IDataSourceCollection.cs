using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Interface for the management of a collection of entities in a data source
/// </summary>
public interface IDataSourceCollection<in TDocument> where TDocument : RtEntity, new()
{
    /// <summary>
    /// Inserts a new document into the collection
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="document">The document to insert</param>
    /// <returns></returns>
    Task InsertAsync(IOctoSession session, TDocument document);
}