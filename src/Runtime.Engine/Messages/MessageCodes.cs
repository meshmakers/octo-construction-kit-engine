//------------------------------------------------------------------------------
// <auto-generate>
//     The code was generated from a template.
//
//     Modifications to this file may result in incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.Runtime.Engine.Messages;

/// <summary>
///     Defines possible messages for:
///     Information
///     Warnings
///     Errors
/// </summary>
[DebuggerNonUserCode]
[CompilerGenerated]
internal static class MessageCodes
{
    private static readonly Dictionary<string, OperationMessageTemplate> Templates = new()
    {
        {
            "SchemaValidationError",
            new OperationMessageTemplate(MessageLevel.FatalError,
                1, "{locationReference}: Schema validation failed: '{errorMessage}'",
                new[] { "locationReference", "errorMessage" })
        },
        {
            "MandatoryAttributeMissing",
            new OperationMessageTemplate(MessageLevel.FatalError,
                2,
                "{tenantId}: Mandatory attribute '{attributeCkAttributeId}' of entity '{reference}' defines no default value and is missing.",
                new[] { "tenantId", "attributeCkAttributeId", "reference" })
        },
        {
            "CkTypeIdNotFound",
            new OperationMessageTemplate(MessageLevel.FatalError,
                3, "{tenantId}: CkTypeId '{rtEntityCkTypeId}' not found.",
                new[] { "tenantId", "rtEntityCkTypeId" })
        },
        {
            "CkTypeIdIsAbstract",
            new OperationMessageTemplate(MessageLevel.FatalError,
                4, "{tenantId}: CkTypeId '{ckTypeId}' is abstract.",
                new[] { "tenantId", "ckTypeId" })
        },
        {
            "MandatoryAttributeMissingAtUpdate",
            new OperationMessageTemplate(MessageLevel.FatalError,
                5, "{tenantId}: Mandatory attribute '{attributeCkAttributeId}' of entity '{rtEntityCkTypeId}@{rtId}' is missing.",
                new[] { "tenantId", "attributeCkAttributeId", "rtEntityCkTypeId", "rtId" })
        },
        {
            "AssociationCardinalityViolationOnCreate",
            new OperationMessageTemplate(MessageLevel.FatalError,
                6,
                "{tenantId}: Entity '{ckTypeId}@{rtId}': Inbound association '{roleId}' has minimum multiplicity of '{multiplicity}'. There is no create statement for creating this association.",
                new[] { "tenantId", "ckTypeId", "rtId", "roleId", "multiplicity" })
        },
        {
            "AssociationNotAllowed",
            new OperationMessageTemplate(MessageLevel.FatalError,
                7, "{tenantId}: '{ckTypeId}@{rtId}': Inbound association '{roleId}' is not allowed.",
                new[] { "tenantId", "ckTypeId", "rtId", "roleId" })
        },
        {
            "MissingTargetEntity",
            new OperationMessageTemplate(MessageLevel.FatalError,
                8, "{tenantId}: Target entity '{ckTypeId}@{rtId}' does not exist.",
                new[] { "tenantId", "ckTypeId", "rtId" })
        },
        {
            "EntityNotFound",
            new OperationMessageTemplate(MessageLevel.FatalError,
                9, "{tenantId}: Entity '{ckTypeId}@{rtId}' does not exist.",
                new[] { "tenantId", "ckTypeId", "rtId" })
        },
        {
            "MissingOriginEntity",
            new OperationMessageTemplate(MessageLevel.FatalError,
                10, "{tenantId}: Origin entity '{ckTypeId}@{rtId}' does not exist.",
                new[] { "tenantId", "ckTypeId", "rtId" })
        },
        {
            "InboundAssociationNotAllowedForCkType",
            new OperationMessageTemplate(MessageLevel.FatalError,
                11,
                "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Inbound association '{roleId}' to CkTypeId '{targetCkTypeId}' is not allowed.",
                new[] { "tenantId", "originCkTypeId", "originRtId", "roleId", "targetCkTypeId" })
        },
        {
            "OutboundAssociationNotAllowedForCkType",
            new OperationMessageTemplate(MessageLevel.FatalError,
                12,
                "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Outbound association '{roleId}' to CkTypeId '{targetCkTypeId}' is not allowed.",
                new[] { "tenantId", "originCkTypeId", "originRtId", "roleId", "targetCkTypeId" })
        },
        {
            "AssociationCardinalityViolationOnDelete",
            new OperationMessageTemplate(MessageLevel.FatalError,
                13,
                "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Association deletion violates the model.",
                new[] { "tenantId", "originCkTypeId", "originRtId", "roleId", "multiplicity" })
        },
        {
            "AssociationCardinalityViolationOnModification",
            new OperationMessageTemplate(MessageLevel.FatalError,
                14,
                "{tenantId}: Entity '{originCkTypeId}@{originRtId}': Inbound association '{roleId}' has maximum multiplicity of '{multiplicity}'. Adding another association violates the model.",
                new[] { "tenantId", "originCkTypeId", "originRtId", "roleId", "multiplicity" })
        },
        {
            "AssociationDoesNotExist",
            new OperationMessageTemplate(MessageLevel.FatalError,
                15,
                "{tenantId}: Association '{roleId}' from entity '{originCkTypeId}@{originRtId}' to entity '{targetCkTypeId}'->'{targetRtId}' does not exist.",
                new[] { "tenantId", "roleId", "originCkTypeId", "originRtId", "targetCkTypeId", "targetRtId" })
        },
        {
            "AssociationAlreadyExists",
            new OperationMessageTemplate(MessageLevel.FatalError,
                16,
                "{tenantId}: Association '{roleId}' from entity '{originCkTypeId}@{originRtId}' to entity '{targetCkTypeId}'->'{targetRtId}' does already exist.",
                new[] { "tenantId", "roleId", "originCkTypeId", "originRtId", "targetCkTypeId", "targetRtId" })
        },
        {
            "RtEntityNeedsToBeDefinedAtInsertUpdateReplace",
            new OperationMessageTemplate(MessageLevel.FatalError,
                17, "{tenantId}: RtEntity '{rtEntityCkTypeId}@{rtId}' needs to be defined at insert, update or replace.",
                new[] { "tenantId", "rtEntityCkTypeId", "rtId" })
        },
        {
            "RtEntityIdAlreadyExistInUpdateList",
            new OperationMessageTemplate(MessageLevel.Error,
                18, "{tenantId}: RtEntity '{rtEntityCkTypeId}@{rtId}' already exists in update list.",
                new[] { "tenantId", "rtEntityCkTypeId", "rtId" })
        },
        {
            "CkRecordIdNotFound",
            new OperationMessageTemplate(MessageLevel.Error,
                19, "{tenantId}: CkRecordId '{ckRecordId}' not found.",
                new[] { "tenantId", "ckRecordId" })
        }
    };

    // ReSharper disable once MemberCanBePrivate.Global
    internal static OperationMessage GetMessage(string messageKey, params object[] args)
    {
        if (!Templates.ContainsKey(messageKey))
        {
            throw new ArgumentOutOfRangeException($"Message with key '{messageKey}' does not exist.");
        }

        return Templates[messageKey].CreateMessage(args);
    }

    internal static OperationMessage SchemaValidationError(object locationReference, object errorMessage)
    {
        return GetMessage("SchemaValidationError", locationReference, errorMessage);
    }

    internal static OperationMessage MandatoryAttributeMissing(object tenantId, object attributeCkAttributeId, object reference)
    {
        return GetMessage("MandatoryAttributeMissing", tenantId, attributeCkAttributeId, reference);
    }

    internal static OperationMessage CkTypeIdNotFound(object tenantId, object rtEntityCkTypeId)
    {
        return GetMessage("CkTypeIdNotFound", tenantId, rtEntityCkTypeId);
    }

    internal static OperationMessage CkTypeIdIsAbstract(object tenantId, object ckTypeId)
    {
        return GetMessage("CkTypeIdIsAbstract", tenantId, ckTypeId);
    }

    internal static OperationMessage MandatoryAttributeMissingAtUpdate(object tenantId, object attributeCkAttributeId,
        object rtEntityCkTypeId, object rtId)
    {
        return GetMessage("MandatoryAttributeMissingAtUpdate", tenantId, attributeCkAttributeId, rtEntityCkTypeId, rtId);
    }

    internal static OperationMessage AssociationCardinalityViolationOnCreate(object tenantId, object ckTypeId, object rtId, object roleId,
        object multiplicity)
    {
        return GetMessage("AssociationCardinalityViolationOnCreate", tenantId, ckTypeId, rtId, roleId, multiplicity);
    }

    internal static OperationMessage AssociationNotAllowed(object tenantId, object ckTypeId, object rtId, object roleId)
    {
        return GetMessage("AssociationNotAllowed", tenantId, ckTypeId, rtId, roleId);
    }

    internal static OperationMessage MissingTargetEntity(object tenantId, object ckTypeId, object rtId)
    {
        return GetMessage("MissingTargetEntity", tenantId, ckTypeId, rtId);
    }

    internal static OperationMessage EntityNotFound(object tenantId, object ckTypeId, object rtId)
    {
        return GetMessage("EntityNotFound", tenantId, ckTypeId, rtId);
    }

    internal static OperationMessage MissingOriginEntity(object tenantId, object ckTypeId, object rtId)
    {
        return GetMessage("MissingOriginEntity", tenantId, ckTypeId, rtId);
    }

    internal static OperationMessage InboundAssociationNotAllowedForCkType(object tenantId, object originCkTypeId, object originRtId,
        object roleId, object targetCkTypeId)
    {
        return GetMessage("InboundAssociationNotAllowedForCkType", tenantId, originCkTypeId, originRtId, roleId, targetCkTypeId);
    }

    internal static OperationMessage OutboundAssociationNotAllowedForCkType(object tenantId, object originCkTypeId, object originRtId,
        object roleId, object targetCkTypeId)
    {
        return GetMessage("OutboundAssociationNotAllowedForCkType", tenantId, originCkTypeId, originRtId, roleId, targetCkTypeId);
    }

    internal static OperationMessage AssociationCardinalityViolationOnDelete(object tenantId, object originCkTypeId, object originRtId,
        object roleId, object multiplicity)
    {
        return GetMessage("AssociationCardinalityViolationOnDelete", tenantId, originCkTypeId, originRtId, roleId, multiplicity);
    }

    internal static OperationMessage AssociationCardinalityViolationOnModification(object tenantId, object originCkTypeId,
        object originRtId, object roleId, object multiplicity)
    {
        return GetMessage("AssociationCardinalityViolationOnModification", tenantId, originCkTypeId, originRtId, roleId, multiplicity);
    }

    internal static OperationMessage AssociationDoesNotExist(object tenantId, object roleId, object originCkTypeId, object originRtId,
        object targetCkTypeId, object targetRtId)
    {
        return GetMessage("AssociationDoesNotExist", tenantId, roleId, originCkTypeId, originRtId, targetCkTypeId, targetRtId);
    }

    internal static OperationMessage AssociationAlreadyExists(object tenantId, object roleId, object originCkTypeId, object originRtId,
        object targetCkTypeId, object targetRtId)
    {
        return GetMessage("AssociationAlreadyExists", tenantId, roleId, originCkTypeId, originRtId, targetCkTypeId, targetRtId);
    }

    internal static OperationMessage RtEntityNeedsToBeDefinedAtInsertUpdateReplace(object tenantId, object rtEntityCkTypeId, object rtId)
    {
        return GetMessage("RtEntityNeedsToBeDefinedAtInsertUpdateReplace", tenantId, rtEntityCkTypeId, rtId);
    }

    internal static OperationMessage RtEntityIdAlreadyExistInUpdateList(object tenantId, object rtEntityCkTypeId, object rtId)
    {
        return GetMessage("RtEntityIdAlreadyExistInUpdateList", tenantId, rtEntityCkTypeId, rtId);
    }

    internal static OperationMessage CkRecordIdNotFound(object tenantId, object ckRecordId)
    {
        return GetMessage("CkRecordIdNotFound", tenantId, ckRecordId);
    }
}