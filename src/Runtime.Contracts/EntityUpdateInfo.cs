using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Interface for an entity update info.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public interface IEntityUpdateInfo<out TEntity> where TEntity : RtEntity
{
    /// <summary>
    ///     Entity for modification.
    /// </summary>
    public TEntity? RtEntity { get; }

    /// <summary>
    ///     Runtime Identifier of an existing entity.
    /// </summary>
    public OctoObjectId? RtId { get; }
    
    /// <summary>
    ///     Construction Kit Type Identifier of entity to be modified.
    /// </summary>
    public CkId<CkTypeId> CkTypeId { get; }

    /// <summary>
    ///     MOD option.
    /// </summary>
    public EntityModOptions ModOption { get; }

    /// <summary>
    /// Gets the runtime entity identifier.
    /// </summary>
    /// <returns></returns>
    RtEntityId GetRtEntityId();
}

/// <summary>
///     Represents an entity update info.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class EntityUpdateInfo<TEntity> : IEntityUpdateInfo<TEntity>
    where TEntity : RtEntity
{
    [JsonConstructor]
    private EntityUpdateInfo(OctoObjectId? rtId, CkId<CkTypeId> ckTypeId, TEntity? rtEntity, EntityModOptions modOption)
    {
        RtId = rtId;
        CkTypeId = ckTypeId;
        ModOption = modOption;
        RtEntity = rtEntity;
    }
    
    private EntityUpdateInfo(RtEntityId rtEntityId, TEntity rtEntity, EntityModOptions modOption)
        : this(rtEntityId, modOption)
    {
        RtEntity = rtEntity;
    }
    
    private EntityUpdateInfo(CkId<CkTypeId> ckTypeId, TEntity rtEntity, EntityModOptions modOption)
    {
        CkTypeId = ckTypeId;
        RtEntity = rtEntity;
        ModOption = modOption;
    }

    private EntityUpdateInfo(RtEntityId rtEntityId, EntityModOptions modOption)
    {
        RtId = rtEntityId.RtId;
        CkTypeId = rtEntityId.CkTypeId;
        ModOption = modOption;
    }

    /// <inheritdoc />
    public TEntity? RtEntity { get; }

    /// <inheritdoc />
    public OctoObjectId? RtId { get; }
    
    /// <inheritdoc />
    public CkId<CkTypeId> CkTypeId { get; }

    /// <summary>
    ///     MOD option.
    /// </summary>
    public EntityModOptions ModOption { get; }

    /// <inheritdoc />
    public RtEntityId GetRtEntityId()
    {
        if (RtId == null)
        {
            throw new InvalidOperationException("RtId is null");
        }
        
        return new RtEntityId(CkTypeId, RtId.Value);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for insert.
    /// </summary>
    /// <param name="ckTypeId">Type identifier of the construction kit</param>
    /// <param name="rtEntity">Runtime entity to insert</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateInsert(CkId<CkTypeId> ckTypeId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(ckTypeId, rtEntity, EntityModOptions.Insert);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for insert.
    /// </summary>
    /// <param name="rtEntity">Runtime entity to insert</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateInsert(TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntity.ToRtEntityId(), rtEntity, EntityModOptions.Insert);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for delete.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateDelete(RtEntityId rtEntityId)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, EntityModOptions.Delete);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for update.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to update</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateUpdate(RtEntityId rtEntityId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Update);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for replace.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to replace</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateReplace(RtEntityId rtEntityId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Replace);
    }
}