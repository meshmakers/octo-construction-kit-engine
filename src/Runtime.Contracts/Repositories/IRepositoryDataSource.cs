using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Interface for the data source of a runtime repository
/// </summary>
public interface IRepositoryDataSource
{
    /// <summary>
    /// Returns the data source access object for the given entity type
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <typeparam name="TEntity">The type of entity derived from &lt;see cref="RtEntity"/&gt;</typeparam>
    /// <returns></returns>
    IDataSourceCollection<TEntity> GetRtCollection<TEntity>(CkId<CkTypeId> ckTypeId) where TEntity : RtEntity, new();
    
    /// <summary>
    /// Returns the data source access object for the given entity type
    /// </summary>
    /// <typeparam name="TEntity">The type of entity derived from &lt;see cref="RtEntity"/&gt;</typeparam>
    /// <returns></returns>
    IDataSourceCollection<TEntity> GetRtCollection<TEntity>() where TEntity : RtEntity, new();
}