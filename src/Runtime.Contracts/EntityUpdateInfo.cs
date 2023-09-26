using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Generic version of a entity update info
/// </summary>
public class EntityUpdateInfo : EntityUpdateInfo<RtEntity>
{
    /// <summary>
    /// Creates a new instance of <see cref="EntityUpdateInfo"/>.
    /// </summary>
    /// <param name="rtEntity"></param>
    /// <param name="modOption"></param>
    public EntityUpdateInfo(RtEntity rtEntity, EntityModOptions modOption)
        : base(rtEntity, modOption)
    {
    }
}

/// <summary>
/// Represents a entity update info.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class EntityUpdateInfo<TEntity> 
    where TEntity : RtEntity
{
    /// <summary>
    /// Creates a new instance of <see cref="EntityUpdateInfo{TEntity}"/>.
    /// </summary>
    /// <param name="rtEntity"></param>
    /// <param name="modOption"></param>
    public EntityUpdateInfo(TEntity rtEntity, EntityModOptions modOption)
    {
        RtEntity = rtEntity;
        ModOption = modOption;
    }

    /// <summary>
    /// Entity for modification.
    /// </summary>
    public TEntity RtEntity { get; }

    /// <summary>
    /// MOD option.
    /// </summary>
    public EntityModOptions ModOption { get; }
}
