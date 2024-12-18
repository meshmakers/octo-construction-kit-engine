using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

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