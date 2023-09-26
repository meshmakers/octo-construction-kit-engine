using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace Meshmakers.Octo.SystematizedData.Persistence;

public static class RtEntityIdExtensions
{
    public static RtEntityId ToRtEntityId(this RtEntity rtEntity)
    {
        return new RtEntityId(rtEntity.CkTypeId, rtEntity.RtId);
    }
}
