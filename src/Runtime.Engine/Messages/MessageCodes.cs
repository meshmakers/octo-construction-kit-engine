//------------------------------------------------------------------------------
// <auto-generate>
//     The code was generated from a template.
//
//     Modifications to this file may result in incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.Runtime.Engine.Messages;

/// <summary>
/// Defines possible messages for:
/// Information
/// Warnings
/// Errors
/// </summary>
[System.Diagnostics.DebuggerNonUserCodeAttribute()]
[System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
internal static class MessageCodes
{
    // ReSharper disable once MemberCanBePrivate.Global
    internal static OperationMessage GetMessage(string messageKey, string? location, params object[] args)
    {
        if (!Templates.ContainsKey(messageKey))
        {
            throw new ArgumentOutOfRangeException($"Message with key '{messageKey}' does not exist.");
        }
        return Templates[messageKey].CreateMessage(location, args);
    }

    internal static OperationMessage SchemaValidationError(string? location, object path, object errorMessage) =>
        GetMessage("SchemaValidationError", location, path, errorMessage);

    internal static OperationMessage MandatoryAttributeMissing(string? location, object tenantId, object attributeCkAttributeId, object reference) =>
        GetMessage("MandatoryAttributeMissing", location, tenantId, attributeCkAttributeId, reference);

    internal static OperationMessage CkTypeIdNotFound(string? location, object tenantId, object rtEntityCkTypeId) =>
        GetMessage("CkTypeIdNotFound", location, tenantId, rtEntityCkTypeId);

    internal static OperationMessage CkTypeIdIsAbstract(string? location, object tenantId, object ckTypeId) =>
        GetMessage("CkTypeIdIsAbstract", location, tenantId, ckTypeId);

    internal static OperationMessage MandatoryAttributeMissingAtUpdate(string? location, object tenantId, object attributeCkAttributeId, object rtEntityCkTypeId, object rtId) =>
        GetMessage("MandatoryAttributeMissingAtUpdate", location, tenantId, attributeCkAttributeId, rtEntityCkTypeId, rtId);

    internal static OperationMessage AssociationCardinalityViolationOnCreate(string? location, object tenantId, object ckTypeId, object roleId, object multiplicity) =>
        GetMessage("AssociationCardinalityViolationOnCreate", location, tenantId, ckTypeId, roleId, multiplicity);

    internal static OperationMessage AssociationNotAllowed(string? location, object tenantId, object ckTypeId, object rtId, object roleId) =>
        GetMessage("AssociationNotAllowed", location, tenantId, ckTypeId, rtId, roleId);

    internal static OperationMessage MissingTargetEntity(string? location, object tenantId, object ckTypeId, object rtId) =>
        GetMessage("MissingTargetEntity", location, tenantId, ckTypeId, rtId);

    internal static OperationMessage EntityNotFound(string? location, object tenantId, object ckTypeId, object rtId) =>
        GetMessage("EntityNotFound", location, tenantId, ckTypeId, rtId);

    internal static OperationMessage MissingOriginEntity(string? location, object tenantId, object ckTypeId, object rtId) =>
        GetMessage("MissingOriginEntity", location, tenantId, ckTypeId, rtId);

    internal static OperationMessage InboundAssociationNotAllowedForCkType(string? location, object tenantId, object originCkTypeId, object originRtId, object roleId, object targetCkTypeId) =>
        GetMessage("InboundAssociationNotAllowedForCkType", location, tenantId, originCkTypeId, originRtId, roleId, targetCkTypeId);

    internal static OperationMessage OutboundAssociationNotAllowedForCkType(string? location, object tenantId, object originCkTypeId, object originRtId, object roleId, object targetCkTypeId) =>
        GetMessage("OutboundAssociationNotAllowedForCkType", location, tenantId, originCkTypeId, originRtId, roleId, targetCkTypeId);

    internal static OperationMessage AssociationCardinalityViolationOnDelete(string? location, object tenantId, object originCkTypeId, object originRtId, object roleId, object multiplicity) =>
        GetMessage("AssociationCardinalityViolationOnDelete", location, tenantId, originCkTypeId, originRtId, roleId, multiplicity);

    internal static OperationMessage AssociationCardinalityViolationOnModification(string? location, object tenantId, object originCkTypeId, object originRtId, object roleId, object multiplicity) =>
        GetMessage("AssociationCardinalityViolationOnModification", location, tenantId, originCkTypeId, originRtId, roleId, multiplicity);

    internal static OperationMessage AssociationDoesNotExist(string? location, object tenantId, object roleId, object originCkTypeId, object originRtId, object targetCkTypeId, object targetRtId) =>
        GetMessage("AssociationDoesNotExist", location, tenantId, roleId, originCkTypeId, originRtId, targetCkTypeId, targetRtId);

    internal static OperationMessage AssociationAlreadyExists(string? location, object tenantId, object roleId, object originCkTypeId, object originRtId, object targetCkTypeId, object targetRtId) =>
        GetMessage("AssociationAlreadyExists", location, tenantId, roleId, originCkTypeId, originRtId, targetCkTypeId, targetRtId);

    internal static OperationMessage RtEntityNeedsToBeDefinedAtUpdateReplace(string? location, object tenantId, object rtEntityCkTypeId, object rtId) =>
        GetMessage("RtEntityNeedsToBeDefinedAtUpdateReplace", location, tenantId, rtEntityCkTypeId, rtId);

    internal static OperationMessage RtEntityIdAlreadyExistInUpdateList(string? location, object tenantId, object rtEntityCkTypeId, object rtId) =>
        GetMessage("RtEntityIdAlreadyExistInUpdateList", location, tenantId, rtEntityCkTypeId, rtId);

    internal static OperationMessage CkRecordIdNotFound(string? location, object tenantId, object ckRecordId) =>
        GetMessage("CkRecordIdNotFound", location, tenantId, ckRecordId);

    internal static OperationMessage RtEntityNeedsToBeDefinedAtInsert(string? location, object tenantId, object rtEntityCkTypeId) =>
        GetMessage("RtEntityNeedsToBeDefinedAtInsert", location, tenantId, rtEntityCkTypeId);

    private static readonly Dictionary<string, OperationMessageTemplate> Templates = new()
    {
        {
            "SchemaValidationError",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 1, "Schema validation failed at '{path}'->'{errorMessage}'",
                 new [] {"path", "errorMessage"})
        },
        {
            "MandatoryAttributeMissing",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 2, "{tenantId}: Mandatory attribute '{attributeCkAttributeId}' of entity '{reference}' defines no default value and is missing.",
                 new [] {"tenantId", "attributeCkAttributeId", "reference"})
        },
        {
            "CkTypeIdNotFound",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 3, "{tenantId}: CkTypeId '{rtEntityCkTypeId}' not found.",
                 new [] {"tenantId", "rtEntityCkTypeId"})
        },
        {
            "CkTypeIdIsAbstract",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 4, "{tenantId}: CkTypeId '{ckTypeId}' is abstract.",
                 new [] {"tenantId", "ckTypeId"})
        },
        {
            "MandatoryAttributeMissingAtUpdate",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 5, "{tenantId}: Mandatory attribute '{attributeCkAttributeId}' of entity '{rtEntityCkTypeId}@{rtId}' is missing.",
                 new [] {"tenantId", "attributeCkAttributeId", "rtEntityCkTypeId", "rtId"})
        },
        {
            "AssociationCardinalityViolationOnCreate",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 6, "{tenantId}: Entity with CkTypeId '{ckTypeId}': Inbound association '{roleId}' has minimum multiplicity of '{multiplicity}'. There is no create statement for creating this association.",
                 new [] {"tenantId", "ckTypeId", "roleId", "multiplicity"})
        },
        {
            "AssociationNotAllowed",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 7, "{tenantId}: '{ckTypeId}@{rtId}': Inbound association '{roleId}' is not allowed.",
                 new [] {"tenantId", "ckTypeId", "rtId", "roleId"})
        },
        {
            "MissingTargetEntity",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 8, "{tenantId}: Target entity '{ckTypeId}@{rtId}' does not exist.",
                 new [] {"tenantId", "ckTypeId", "rtId"})
        },
        {
            "EntityNotFound",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 9, "{tenantId}: Entity '{ckTypeId}@{rtId}' does not exist.",
                 new [] {"tenantId", "ckTypeId", "rtId"})
        },
        {
            "MissingOriginEntity",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 10, "{tenantId}: Origin entity '{ckTypeId}@{rtId}' does not exist.",
                 new [] {"tenantId", "ckTypeId", "rtId"})
        },
        {
            "InboundAssociationNotAllowedForCkType",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 11, "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Inbound association '{roleId}' to CkTypeId '{targetCkTypeId}' is not allowed.",
                 new [] {"tenantId", "originCkTypeId", "originRtId", "roleId", "targetCkTypeId"})
        },
        {
            "OutboundAssociationNotAllowedForCkType",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 12, "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Outbound association '{roleId}' to CkTypeId '{targetCkTypeId}' is not allowed.",
                 new [] {"tenantId", "originCkTypeId", "originRtId", "roleId", "targetCkTypeId"})
        },
        {
            "AssociationCardinalityViolationOnDelete",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 13, "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Association deletion violates the model.",
                 new [] {"tenantId", "originCkTypeId", "originRtId", "roleId", "multiplicity"})
        },
        {
            "AssociationCardinalityViolationOnModification",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 14, "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Adding another association violates the model.",
                 new [] {"tenantId", "originCkTypeId", "originRtId", "roleId", "multiplicity"})
        },
        {
            "AssociationDoesNotExist",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 15, "{tenantId}: Association '{roleId}' from entity '{originCkTypeId}@{originRtId}' to entity '{targetCkTypeId}'->'{targetRtId}' does not exist.",
                 new [] {"tenantId", "roleId", "originCkTypeId", "originRtId", "targetCkTypeId", "targetRtId"})
        },
        {
            "AssociationAlreadyExists",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 16, "{tenantId}: Association '{roleId}' from entity '{originCkTypeId}@{originRtId}' to entity '{targetCkTypeId}'->'{targetRtId}' does already exist.",
                 new [] {"tenantId", "roleId", "originCkTypeId", "originRtId", "targetCkTypeId", "targetRtId"})
        },
        {
            "RtEntityNeedsToBeDefinedAtUpdateReplace",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 17, "{tenantId}: RtEntity '{rtEntityCkTypeId}@{rtId}' needs to be defined at update or replace.",
                 new [] {"tenantId", "rtEntityCkTypeId", "rtId"})
        },
        {
            "RtEntityIdAlreadyExistInUpdateList",
             new OperationMessageTemplate(MessageLevel.Error,
                 18, "{tenantId}: RtEntity '{rtEntityCkTypeId}@{rtId}' already exists in update list.",
                 new [] {"tenantId", "rtEntityCkTypeId", "rtId"})
        },
        {
            "CkRecordIdNotFound",
             new OperationMessageTemplate(MessageLevel.Error,
                 19, "{tenantId}: CkRecordId '{ckRecordId}' not found.",
                 new [] {"tenantId", "ckRecordId"})
        },
        {
            "RtEntityNeedsToBeDefinedAtInsert",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 20, "{tenantId}: RtEntity of CkTypeId '{rtEntityCkTypeId}' needs to be defined at insert.",
                 new [] {"tenantId", "rtEntityCkTypeId"})
        },
    };
}

