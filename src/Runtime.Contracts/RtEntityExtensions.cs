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
        var ckTypeId = rtEntity.GetRtCkTypeId();
        return new RtEntityId(ckTypeId, rtEntity.RtId);
    }

    /// <summary>
    ///     Returns the corresponding <see cref="CkId{T}" /> of the <see cref="RtEntity" />.
    /// </summary>
    /// <param name="rtEntity"></param>
    /// <typeparam name="TEntity"></typeparam>
    /// <returns></returns>
    public static RtCkId<CkTypeId> GetRtCkTypeId<TEntity>(this TEntity rtEntity)
        where TEntity : RtEntity
    {
        if (rtEntity.CkTypeId != null)
        {
            return rtEntity.CkTypeId;
        }

        return GetRtCkTypeId(rtEntity.GetType());
    }

    /// <summary>
    ///     Returns the corresponding <see cref="RtCkId{T}" /> of the <see cref="RtEntity" />.
    /// </summary>
    /// <remarks>
    ///     See also <see cref="RtCkIdAttribute" />, this attribute is used to determine the CkTypeId of the entity.
    /// </remarks>
    /// <typeparam name="TEntity"></typeparam>
    /// <returns></returns>
    public static RtCkId<CkTypeId> GetRtCkTypeId<TEntity>()
        where TEntity : RtEntity
    {
        return GetRtCkTypeId(typeof(TEntity));
    }

    private static RtCkId<CkTypeId> GetRtCkTypeId(Type type)
    {
        var ckTypeId = TryGetGetCkTypeId(type);
        if (ckTypeId == null)
        {
            throw PersistenceException.CkTypeIdNotSet(type);
        }

        return ckTypeId;
    }
    
    private static RtCkId<CkTypeId>? TryGetGetCkTypeId(Type type)
    {
        var customAttribute = Attribute.GetCustomAttribute(type, typeof(RtCkIdAttribute));
        if (customAttribute == null)
        {
            return null;
        }

        var ckIdAttribute = (RtCkIdAttribute)customAttribute;
        return ckIdAttribute.RtCkId;
    }
}