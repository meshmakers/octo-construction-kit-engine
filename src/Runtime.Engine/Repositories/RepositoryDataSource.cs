using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
/// Base class for a data source for a repository
/// </summary>
public abstract class RepositoryDataSource : IRepositoryDataSource
{
    /// <inheritdoc />
    public abstract IDataSourceCollection<TEntity> GetRtCollection<TEntity>(CkId<CkTypeId> ckTypeId) where TEntity : RtEntity, new();

    /// <inheritdoc />
    public IDataSourceCollection<TEntity> GetRtCollection<TEntity>() where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        return GetRtCollection<TEntity>(ckTypeId);
    }
}