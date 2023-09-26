using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace Meshmakers.Octo.SystematizedData.Persistence;

public class EntityUpdateInfo : EntityUpdateInfo<RtEntity>
{
    public EntityUpdateInfo(RtEntity rtEntity, EntityModOptions modOption)
        : base(rtEntity, modOption)
    {
    }
}

public class EntityUpdateInfo<TEntity> 
    where TEntity : RtEntity
{
    public EntityUpdateInfo(TEntity rtEntity, EntityModOptions modOption)
    {
        RtEntity = rtEntity;
        ModOption = modOption;
    }

    public TEntity RtEntity { get; }

    public EntityModOptions ModOption { get; }
}
