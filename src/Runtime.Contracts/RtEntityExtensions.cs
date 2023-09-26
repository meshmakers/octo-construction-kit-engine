using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.DataAccess;
using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace Meshmakers.Octo.SystematizedData.Persistence;

public static class RtEntityExtensions
{
    public static CkId<CkTypeId> GetCkTypeId<TEntity>(this TEntity rtEntity)
        where TEntity : RtEntity
    {
        if (!string.IsNullOrWhiteSpace(rtEntity.CkTypeId.Key.TypeId))
        {
            return rtEntity.CkTypeId;
        }

        return GetCkTypeId(rtEntity.GetType());
    }

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
            throw new InvalidCkTypeIdException(
                $"Type '{type}' does not define attribute '{typeof(CkIdAttribute)}'");
        }

        var ckIdAttribute = (CkIdAttribute)customAttribute;
        return ckIdAttribute.CkId;
    }
}