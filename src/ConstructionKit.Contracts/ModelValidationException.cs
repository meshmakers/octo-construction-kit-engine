using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

public class ModelValidationException : CkModelException
{
    public ModelValidationException()
    {
    }

    public ModelValidationException(string message) : base(message)
    {
    }

    public ModelValidationException(string message, Exception inner) : base(message, inner)
    {
    }
    
    public static Exception DuplicateAttributeIds(IEnumerable<CkId<CkAttributeId>> duplicateAttributes)
    {
        var attributeIds = string.Join(", ", duplicateAttributes);
        return new ModelValidationException($"Following attribute ids are duplicates: '{attributeIds}'");
    }

    public static Exception UnknownCkTypeIdForInheritance(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException($"CkTypeId '{ckTypeId}' is unknown for inheritance. This may happen because a dependency to another construction kit model is missing.");
    }

    public static Exception UnknownCkTypeId(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException($"CkTypeId '{ckTypeId}' is unknown. This may happen because a dependency to another construction kit model is missing.");
    }
    
    public static Exception CkTypeIdAlreadyExistsInDatabase(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException($"CkTypeId '{ckTypeId}' already exists in database.");
    }

    public static Exception UnknownAttributeOfCkTypeIdInSource(CkId<CkTypeId> ckTypeId, CkId<CkAttributeId> attributeId)
    {
        return new ModelValidationException($"Attribute Id '{attributeId}' of CkTypeId '{ckTypeId}' does not exist.");
    }

    public static Exception CommonValidationFailed(string error)
    {
        return new ModelValidationException($"Validation of Construction Kit Model failed:" + Environment.NewLine + error);
    }

    public static Exception DuplicateAttributeIdsInCkType(CkId<CkTypeId> ckTypeId, IEnumerable<CkId<CkAttributeId>> duplicateAttributeIds)
    {
        var attributeIds = string.Join(", ", duplicateAttributeIds);
        return new ModelValidationException($"CkTypeId '{ckTypeId}' has duplicate attribute IDs: '{attributeIds}'");
    }

    public static Exception DuplicateAttributeNamesInCkType(CkId<CkTypeId> ckTypeId, IEnumerable<string> select)
    {
        var attributeNames = string.Join(", ", select);
        return new ModelValidationException($"CkTypeId '{ckTypeId}' has duplicate attribute names: '{attributeNames}'");
    }

    public static Exception CkTypeIdUsingSystemReservedAttributeNames(CkId<CkTypeId> ckTypeId, IEnumerable<string> systemReservedAttributeNames)
    {
        var attributeNames = string.Join(", ", systemReservedAttributeNames);
        return new ModelValidationException(
            $"CkTypeId '{ckTypeId}' using attribute names that are system reserved: '{attributeNames}'");
    }

    public static Exception CkAssociationRoleNotFound(CkId<CkAssociationRoleId> associationId)
    {
        return new ModelValidationException($"Association role '{associationId}' not found.");
    }

    public static Exception UnknownCkModel(CkModelId modelDependency)
    {
       return new ModelValidationException($"Repository does not contain construction kit model '{modelDependency}'.");
    }


    public static Exception MissingTargetEntity(RtEntityId rtEntityId)
    {
        return new ModelValidationException($"Target entity '{rtEntityId}' does not exist.");
    }
    
    public static Exception MissingOriginEntity(RtEntityId rtEntityId)
    {
        return new ModelValidationException($"Origin entity '{rtEntityId}' does not exist.");
    }

    public static Exception AssociationNotAllowed(CkId<CkAssociationRoleId> roleId, RtEntityId rtEntityId)
    {
        return new ModelValidationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' is not allowed.");
    }
    
    public static Exception InboundAssociationNotAllowedForCkType(CkId<CkAssociationRoleId> roleId, RtEntityId originRtEntityId, CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{originRtEntityId.CkTypeId}'->RtId '{originRtEntityId.RtId}': Inbound association '{roleId}' to CkTypeId '{ckTypeId}' is not allowed.");
    }
    
    public static Exception OutboundAssociationNotAllowedForCkType(CkId<CkAssociationRoleId> roleId, RtEntityId originRtEntityId, CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{originRtEntityId.CkTypeId}'->RtId '{originRtEntityId.RtId}': Outbound association '{roleId}' to CkTypeId '{ckTypeId}' is not allowed.");
    }
    
    public static Exception AssociationCardinalityViolationOnCreate(CkId<CkAssociationRoleId> roleId, MultiplicitiesDto multiplicity, RtEntityId rtEntityId)
    {
        return new ModelValidationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' has minimum multiplicity of '{multiplicity}'. There is no create statement for creating this association.");
    }

    
    public static Exception AssociationCardinalityViolationOnDelete(CkId<CkAssociationRoleId> roleId, MultiplicitiesDto multiplicity, RtEntityId rtEntityId)
    {
        return new ModelValidationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Association deletion violates the model.");
    }
    
    public static Exception AssociationCardinalityViolationOnModification(CkId<CkAssociationRoleId> roleId, MultiplicitiesDto multiplicity, RtEntityId rtEntityId)
    {
        return new ModelValidationException(
            $"CkTypeId '{rtEntityId.CkTypeId}'->RtId '{rtEntityId.RtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Adding another association violates the model.");
    }

    public static Exception UnknownCkTypeIdForAssociationTarget(CkId<CkTypeId> originCkTypeId, CkId<CkAssociationRoleId> entityAssociationRoleId, CkId<CkTypeId> typeAssociationTargetCkTypeId)
    {
        return new ModelValidationException($"CkTypeId '{originCkTypeId}' defines a unknown target construction kit type id '{typeAssociationTargetCkTypeId}' for role id '{entityAssociationRoleId}'." +
                                            $" This may happen because a dependency to another construction kit model is missing.");
    }

    public static Exception DerivedFromCkTypeIdThatIsFinal(CkId<CkTypeId> currentCkTypeId, CkId<CkTypeId> lastCkTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{currentCkTypeId}' is final, but CkTypeId '{lastCkTypeId}' is derived from it.");
    }

    public static Exception InheritanceMissing(string typeId)
    {
        return new ModelValidationException($"TypeId '{typeId}' has no inheritance definition. Ensure that attribute ckDerivedId is set.");
    }

    public static Exception ModelIdContainsInvalidCharacters(string modelId)
    {
        return new ModelValidationException($"ModelId '{modelId}' contains invalid characters. Only a-z, A-Z, 0-9, _ and . are allowed.");
    }
}

