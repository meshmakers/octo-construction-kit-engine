using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

public class RtAssociation
{
    /// <summary>
    ///     Returns the mongodb ID
    /// </summary>
    public OctoObjectId AssociationId { get; set; }

    public OctoObjectId OriginRtId { get; set; }

    public CkId<CkTypeId> OriginCkTypeId { get; set; }

    public OctoObjectId TargetRtId { get; set; }

    public CkId<CkTypeId> TargetCkTypeId { get; set; }

    public CkId<CkAssociationRoleId> AssociationRoleId { get; set; }
}
