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
    public RtCkId<CkTypeId> CkTypeId { get; }

    /// <summary>
    ///     MOD option.
    /// </summary>
    public EntityModOptions ModOption { get; }

    /// <summary>
    ///     Optional optimistic-concurrency guard applied to <see cref="EntityModOptions.Update" />
    ///     operations. When set, the write is skipped if the guard does not match — see
    ///     <see cref="AttributeNewerThanGuard" />. Ignored for non-update mod options.
    /// </summary>
    public AttributeNewerThanGuard? UpdateGuard { get; }

    /// <summary>
    ///     Names (PascalCase, as in <see cref="RtTypeWithAttributes.Attributes" />) of top-level
    ///     <c>Secret</c> attributes to clear explicitly (AB#5532, concept §4.3 <c>clearSecretAttributes</c>).
    ///     Clearing is the only way to remove a stored secret through an update: an omitted, <c>""</c>
    ///     or (at the API) <c>null</c> secret means "unchanged". Applies to
    ///     <see cref="EntityModOptions.Update" /> and <see cref="EntityModOptions.Replace" />; on
    ///     <see cref="EntityModOptions.Insert" /> a listed attribute is simply not set. The rule engine
    ///     rejects a name that is not a Secret attribute of the type, a required secret, and a
    ///     non-empty value for the same attribute in the same operation.
    ///     <c>null</c> or empty = nothing to clear.
    /// </summary>
    public IReadOnlyCollection<string>? ClearSecretAttributes => null;

    /// <summary>
    /// Gets the runtime entity identifier.
    /// </summary>
    /// <returns></returns>
    RtEntityId GetRtEntityId();
}