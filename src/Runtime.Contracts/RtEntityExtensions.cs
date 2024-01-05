using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Extension methods of <see cref="RtEntity" />.
/// </summary>
public static class RtEntityExtensions
{
    /// <summary>
    ///     Returns the runtime entity id of the <see cref="RtEntity" />.
    /// </summary>
    /// <param name="rtEntity"></param>
    /// <returns></returns>
    public static RtEntityId ToRtEntityId(this RtEntity rtEntity)
    {
        return new RtEntityId(rtEntity.CkTypeId, rtEntity.RtId);
    }

    /// <summary>
    ///     Returns the corresponding <see cref="CkId{T}" /> of the <see cref="RtEntity" />.
    /// </summary>
    /// <param name="rtEntity"></param>
    /// <typeparam name="TEntity"></typeparam>
    /// <returns></returns>
    public static CkId<CkTypeId> GetCkTypeId<TEntity>(this TEntity rtEntity)
        where TEntity : RtEntity
    {
        if (!string.IsNullOrWhiteSpace(rtEntity.CkTypeId.Key.TypeId))
        {
            return rtEntity.CkTypeId;
        }

        return GetCkTypeId(rtEntity.GetType());
    }

    /// <summary>
    ///     Returns the corresponding <see cref="CkId{T}" /> of the <see cref="RtEntity" />.
    /// </summary>
    /// <remarks>
    ///     See also <see cref="CkIdAttribute" />, this attribute is used to determine the CkTypeId of the entity.
    /// </remarks>
    /// <typeparam name="TEntity"></typeparam>
    /// <returns></returns>
    public static CkId<CkTypeId> GetCkTypeId<TEntity>()
        where TEntity : RtEntity
    {
        return GetCkTypeId(typeof(TEntity));
    }

    private static CkId<CkTypeId> GetCkTypeId(Type type)
    {
        var customAttribute = Attribute.GetCustomAttribute(type, typeof(CkIdAttribute));
        if (customAttribute == null)
        {
            throw PersistenceException.CkIdAttributeNotSet(type);
        }

        var ckIdAttribute = (CkIdAttribute)customAttribute;
        return ckIdAttribute.CkId;
    }
}