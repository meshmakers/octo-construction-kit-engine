
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
    internal static OperationMessage GetMessage(string messageKey, params object[] args)
    {
        if (!Templates.ContainsKey(messageKey))
        {
            throw new ArgumentOutOfRangeException($"Message with key '{messageKey}' does not exist.");
        }
        return Templates[messageKey].CreateMessage(args);
    }

    internal static OperationMessage SchemaValidationError(object locationReference, object errorMessage) =>
        GetMessage("SchemaValidationError", locationReference, errorMessage);

    internal static OperationMessage MandatoryAttributeMissing(object tenantId, object attributeCkAttributeId, object rtEntityCkTypeId, object rtId) =>
        GetMessage("MandatoryAttributeMissing", tenantId, attributeCkAttributeId, rtEntityCkTypeId, rtId);

    internal static OperationMessage CkTypeIdNotFound(object tenantId, object rtEntityCkTypeId) =>
        GetMessage("CkTypeIdNotFound", tenantId, rtEntityCkTypeId);

    internal static OperationMessage CkTypeIdIsAbstract(object tenantId, object ckTypeId) =>
        GetMessage("CkTypeIdIsAbstract", tenantId, ckTypeId);

    internal static OperationMessage MandatoryAttributeMissingAtUpdate(object tenantId, object attributeCkAttributeId, object rtEntityCkTypeId, object rtId) =>
        GetMessage("MandatoryAttributeMissingAtUpdate", tenantId, attributeCkAttributeId, rtEntityCkTypeId, rtId);

    private static readonly Dictionary<string, OperationMessageTemplate> Templates = new()
    {
        {
            "SchemaValidationError",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 1, "{locationReference}: Schema validation failed: '{errorMessage}'",
                 new [] {"locationReference", "errorMessage"})
        },
        {
            "MandatoryAttributeMissing",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 2, "{tenantId}: Mandatory attribute '{attributeCkAttributeId}' of entity '{rtEntityCkTypeId}@{rtId}' defines no default value and is missing.",
                 new [] {"tenantId", "attributeCkAttributeId", "rtEntityCkTypeId", "rtId"})
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
    };
}

