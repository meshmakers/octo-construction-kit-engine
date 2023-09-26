using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.SystematizedData.Persistence.CkModel;

public class RuleViolationException : Exception
{
    public RuleViolationException()
    {
    }

    public RuleViolationException(string message) : base(message)
    {
    }

    public RuleViolationException(string message, Exception inner) : base(message, inner)
    {
    }
    
    internal static Exception AssociationCardinalityViolationOnDelete(CkId<CkAssociationRoleId> roleId, MultiplicitiesDto multiplicity, RtEntityId rtEntityId)
    {
        return new RuleViolationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Association deletion violates the model.");
    }
    
    internal static Exception AssociationCardinalityViolationOnModification(CkId<CkAssociationRoleId> roleId, MultiplicitiesDto multiplicity, RtEntityId rtEntityId)
    {
        return new RuleViolationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Adding another association violates the model.");
    }
    
    internal static Exception AssociationNotAllowed(CkId<CkAssociationRoleId> roleId, RtEntityId rtEntityId)
    {
        return new RuleViolationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' is not allowed.");
    }

    internal static Exception MissingTargetEntity(RtEntityId rtEntityId)
    {
        return new RuleViolationException($"Target entity '{rtEntityId}' does not exist.");
    }
    
    internal static Exception AssociationCardinalityViolationOnCreate(CkId<CkAssociationRoleId> roleId, MultiplicitiesDto multiplicity, RtEntityId rtEntityId)
    {
        return new RuleViolationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' has minimum multiplicity of '{multiplicity}'. There is no create statement for creating this association.");
    }
    
    internal static Exception MissingOriginEntity(RtEntityId rtEntityId)
    {
        return new RuleViolationException($"Origin entity '{rtEntityId}' does not exist.");
    }
    
    internal static Exception InboundAssociationNotAllowedForCkType(CkId<CkAssociationRoleId> roleId, RtEntityId originRtEntityId, CkId<CkTypeId> ckTypeId)
    {
        return new RuleViolationException(
            $"CkTypeId '{originRtEntityId.CkTypeId}'->RtId '{originRtEntityId.RtId}': Inbound association '{roleId}' to CkTypeId '{ckTypeId}' is not allowed.");
    }
    
    internal static Exception OutboundAssociationNotAllowedForCkType(CkId<CkAssociationRoleId> roleId, RtEntityId originRtEntityId, CkId<CkTypeId> ckTypeId)
    {
        return new RuleViolationException(
            $"CkTypeId '{originRtEntityId.CkTypeId}'->RtId '{originRtEntityId.RtId}': Outbound association '{roleId}' to CkTypeId '{ckTypeId}' is not allowed.");
    }
    internal static Exception CkAssociationRoleNotFound(CkId<CkAssociationRoleId> associationId)
    {
        return new RuleViolationException($"Association role '{associationId}' not found.");
    }


}
