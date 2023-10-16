using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Interface for a entity update info.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public interface IEntityUpdateInfo<out TEntity> where TEntity : RtEntity
{
    /// <summary>
    /// Entity for modification.
    /// </summary>
    public TEntity? RtEntity { get; }
    
    /// <summary>
    /// Entity id for modification.
    /// </summary>
    public RtEntityId RtEntityId { get; }
    
    /// <summary>
    /// MOD option.
    /// </summary>
    public EntityModOptions ModOption { get; }
}

/// <summary>
/// Represents a entity update info.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class EntityUpdateInfo<TEntity> : IEntityUpdateInfo<TEntity>
    where TEntity : RtEntity
{
    private EntityUpdateInfo(RtEntityId rtEntityId, TEntity rtEntity, EntityModOptions modOption)
    {
        RtEntityId = rtEntityId;
        RtEntity = rtEntity;
        ModOption = modOption;
    }
    
    private EntityUpdateInfo(RtEntityId rtEntityId, EntityModOptions modOption)
    {
        RtEntityId = rtEntityId;
        ModOption = modOption;
    }
    
    /// <summary>
    /// Entity for modification.
    /// </summary>
    public TEntity? RtEntity { get; }
    
    /// <summary>
    /// Entity id for modification.
    /// </summary>
    public RtEntityId RtEntityId { get; }

    /// <summary>
    /// MOD option.
    /// </summary>
    public EntityModOptions ModOption { get; }
    
    /// <summary>
    /// Creates a new instance of <see cref="EntityUpdateInfo{TEntity}"/> for insert.
    /// </summary>
    /// <param name="rtEntity">Runtime entity to insert</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateInsert(TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntity.ToRtEntityId(), rtEntity, EntityModOptions.Insert);
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="EntityUpdateInfo{TEntity}"/> for delete.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateDelete(RtEntityId rtEntityId)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, EntityModOptions.Delete);
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="EntityUpdateInfo{TEntity}"/> for update.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to update</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateUpdate(RtEntityId rtEntityId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Update);
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="EntityUpdateInfo{TEntity}"/> for replace.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to replace</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateReplace(RtEntityId rtEntityId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Replace);
    }
}
