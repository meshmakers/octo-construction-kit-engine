using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.SystematizedData.Persistence;

public class AssociationUpdateInfo
{
    public AssociationUpdateInfo(RtEntityId origin, RtEntityId target,  CkId<CkAssociationRoleId> roleId,
        AssociationModOptionsDto modOption)
    {
        Origin = origin;
        Target = target;
        RoleId = roleId;
        ModOption = modOption;
    }

    public RtEntityId Origin { get; }
    public RtEntityId Target { get; }

    public CkId<CkAssociationRoleId> RoleId { get; }
    public AssociationModOptionsDto ModOption { get; }
}
