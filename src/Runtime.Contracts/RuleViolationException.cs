using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Exception thrown when a rule is violated.
/// </summary>
public class RuleViolationException : Exception
{
    /// <summary>
    /// Creates a new instance of <see cref="RuleViolationException"/>
    /// </summary>
    public RuleViolationException()
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="RuleViolationException"/>
    /// </summary>
    /// <param name="message"></param>
    public RuleViolationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="RuleViolationException"/>
    /// </summary>
    /// <param name="message"></param>
    /// <param name="inner"></param>
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

    internal static Exception AssociationDoesNotExist(CkId<CkAssociationRoleId> dRoleId, RtEntityId origin, RtEntityId target)
    {
        return new RuleViolationException(
            $"Association '{dRoleId}' from '{origin}' to '{target}' does not exist.");
    }

    internal static Exception EntityNotFound(RtEntityId rtEntityId)
    {
        return new RuleViolationException($"Entity '{rtEntityId}' does not exist.");
    }

    internal static Exception AssociationAlreadyExists(CkId<CkAssociationRoleId> roleId, RtEntityId origin, RtEntityId target)
    {
        return new RuleViolationException(
            $"Association '{roleId}' from '{origin}' to '{target}' already exists.");
    }
}
