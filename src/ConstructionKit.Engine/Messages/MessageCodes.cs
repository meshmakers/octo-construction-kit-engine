//------------------------------------------------------------------------------
// <auto-generate>
//     The code was generated from a template.
//
//     Modifications to this file may result in incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Messages;

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

    internal static OperationMessage UnknownCkModel(object modelId) =>
        GetMessage("UnknownCkModel", modelId);

    internal static OperationMessage UnknownAttributeOfCkTypeIdInSource(object ckAttributeId, object ckTypeId) =>
        GetMessage("UnknownAttributeOfCkTypeIdInSource", ckAttributeId, ckTypeId);

    internal static OperationMessage UnknownCkDerivedIdOfCkTypeIdInSource(object derivedCkTypeId, object ckTypeId) =>
        GetMessage("UnknownCkDerivedIdOfCkTypeIdInSource", derivedCkTypeId, ckTypeId);

    internal static OperationMessage UnknownAssociationRoleOfCkTypeIdInSource(object ckTypeId, object roleId) =>
        GetMessage("UnknownAssociationRoleOfCkTypeIdInSource", ckTypeId, roleId);

    internal static OperationMessage UnknownTargetCkTypeIdOfCkTypeIdInSource(object ckTypeId, object targetCkTypeId) =>
        GetMessage("UnknownTargetCkTypeIdOfCkTypeIdInSource", ckTypeId, targetCkTypeId);

    internal static OperationMessage AttributeIdNotUnique(object ckAttributeId) =>
        GetMessage("AttributeIdNotUnique", ckAttributeId);

    internal static OperationMessage AssociationRoleIdNotUnique(object ckAssociationId) =>
        GetMessage("AssociationRoleIdNotUnique", ckAssociationId);

    internal static OperationMessage TypeIdNotUnique(object ckTypeId) =>
        GetMessage("TypeIdNotUnique", ckTypeId);

    internal static OperationMessage InheritanceMissing(object ckTypeId) =>
        GetMessage("InheritanceMissing", ckTypeId);

    internal static OperationMessage CircularDependency(object modelId, object dependentModelId) =>
        GetMessage("CircularDependency", modelId, dependentModelId);

    internal static OperationMessage UnknownCkTypeIdForInheritance(object ckTypeId) =>
        GetMessage("UnknownCkTypeIdForInheritance", ckTypeId);

    internal static OperationMessage CkTypeIdAttributeIdNotUniqueByInheritance(object ckTypeId, object ckAttributeId, object derivedCkTypeId) =>
        GetMessage("CkTypeIdAttributeIdNotUniqueByInheritance", ckTypeId, ckAttributeId, derivedCkTypeId);

    internal static OperationMessage CkTypeIdAttributeNameNotUniqueByInheritance(object ckTypeId, object attributeNames) =>
        GetMessage("CkTypeIdAttributeNameNotUniqueByInheritance", ckTypeId, attributeNames);

    internal static OperationMessage CkTypeIdAssociationNotUnique(object ckTypeId, object ckAssociationId, object targetCkTypeId) =>
        GetMessage("CkTypeIdAssociationNotUnique", ckTypeId, ckAssociationId, targetCkTypeId);

    internal static OperationMessage CkTypeIdAttributeNameNotUnique(object ckTypeId, object attributeName) =>
        GetMessage("CkTypeIdAttributeNameNotUnique", ckTypeId, attributeName);

    internal static OperationMessage CkTypeIdAttributeIdNotUnique(object ckTypeId, object ckAttributeId) =>
        GetMessage("CkTypeIdAttributeIdNotUnique", ckTypeId, ckAttributeId);

    internal static OperationMessage CkTypeIdOutAssociationNotUniqueByInheritance(object ckTypeId, object ckAssociationId, object targetCkTypeId) =>
        GetMessage("CkTypeIdOutAssociationNotUniqueByInheritance", ckTypeId, ckAssociationId, targetCkTypeId);

    internal static OperationMessage CkTypeIdUnknownTargetCkTypeIdForAssociation(object originCkTypeId, object targetCkTypeId, object roleId) =>
        GetMessage("CkTypeIdUnknownTargetCkTypeIdForAssociation", originCkTypeId, targetCkTypeId, roleId);

    internal static OperationMessage CkTypeIdUnknown(object ckTypeId) =>
        GetMessage("CkTypeIdUnknown", ckTypeId);

    internal static OperationMessage CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(object ckTypeId, object ckAssociationId, object targetCkTypeId, object otherCkTypeId, object otherTargetCkTypeId) =>
        GetMessage("CkTypeIdMultipleOutgoingAssociationRepresentingSameRole", ckTypeId, ckAssociationId, targetCkTypeId, otherCkTypeId, otherTargetCkTypeId);

    internal static OperationMessage DerivedFromCkTypeIdThatIsFinal(object baseCkTypeId, object derivedTypeId) =>
        GetMessage("DerivedFromCkTypeIdThatIsFinal", baseCkTypeId, derivedTypeId);

    internal static OperationMessage DirectoryMustBeEmpty(object directory) =>
        GetMessage("DirectoryMustBeEmpty", directory);

    internal static OperationMessage ModelIdContainsInvalidCharacters(object modelId) =>
        GetMessage("ModelIdContainsInvalidCharacters", modelId);

    internal static OperationMessage CkTypeIdContainsInvalidCharacters(object ckTypeId) =>
        GetMessage("CkTypeIdContainsInvalidCharacters", ckTypeId);

    internal static OperationMessage CkAttributeIdContainsInvalidCharacters(object ckAttributeId) =>
        GetMessage("CkAttributeIdContainsInvalidCharacters", ckAttributeId);

    internal static OperationMessage CkAssociationIdContainsInvalidCharacters(object ckAssociationId) =>
        GetMessage("CkAssociationIdContainsInvalidCharacters", ckAssociationId);

    internal static OperationMessage SchemaValidationError(object locationReference, object errorMessage) =>
        GetMessage("SchemaValidationError", locationReference, errorMessage);

    internal static OperationMessage DirectoryDoesNotExist(object directoryPath) =>
        GetMessage("DirectoryDoesNotExist", directoryPath);

    internal static OperationMessage FileDoesNotExist(object filePath) =>
        GetMessage("FileDoesNotExist", filePath);

    internal static OperationMessage SelectionValueNotUnique(object ckEnumId, object key) =>
        GetMessage("SelectionValueNotUnique", ckEnumId, key);

    internal static OperationMessage CkRecordIdUndefined(object ckAttributeId) =>
        GetMessage("CkRecordIdUndefined", ckAttributeId);

    internal static OperationMessage CkRecordIdContainsInvalidCharacters(object ckRecordId) =>
        GetMessage("CkRecordIdContainsInvalidCharacters", ckRecordId);

    internal static OperationMessage RecordIdNotUnique(object ckRecordId) =>
        GetMessage("RecordIdNotUnique", ckRecordId);

    internal static OperationMessage CkRecordIdUnknown(object ckRecordId) =>
        GetMessage("CkRecordIdUnknown", ckRecordId);

    internal static OperationMessage UnknownCkRecordIdForInheritance(object ckRecordId) =>
        GetMessage("UnknownCkRecordIdForInheritance", ckRecordId);

    internal static OperationMessage DerivedFromCkRecordIdThatIsFinal(object baseCkRecordId, object derivedCkRecordId) =>
        GetMessage("DerivedFromCkRecordIdThatIsFinal", baseCkRecordId, derivedCkRecordId);

    internal static OperationMessage CkRecordIdAttributeNameNotUnique(object ckRecordId, object attributeName) =>
        GetMessage("CkRecordIdAttributeNameNotUnique", ckRecordId, attributeName);

    internal static OperationMessage CkRecordIdAttributeIdNotUniqueByInheritance(object ckRecordId, object ckAttributeId, object derivedCkRecordId) =>
        GetMessage("CkRecordIdAttributeIdNotUniqueByInheritance", ckRecordId, ckAttributeId, derivedCkRecordId);

    internal static OperationMessage CkRecordIdAttributeIdNotUnique(object ckRecordId, object ckAttributeId) =>
        GetMessage("CkRecordIdAttributeIdNotUnique", ckRecordId, ckAttributeId);

    internal static OperationMessage CkRecordIdAttributeNameNotUniqueByInheritance(object ckRecordId, object attributeNames) =>
        GetMessage("CkRecordIdAttributeNameNotUniqueByInheritance", ckRecordId, attributeNames);

    internal static OperationMessage AttributeUsesUnknownCkRecordId(object ckAttributeId, object ckRecordId) =>
        GetMessage("AttributeUsesUnknownCkRecordId", ckAttributeId, ckRecordId);

    internal static OperationMessage UnknownAttributeOfCkRecordIdInSource(object ckAttributeId, object ckRecordId) =>
        GetMessage("UnknownAttributeOfCkRecordIdInSource", ckAttributeId, ckRecordId);

    internal static OperationMessage UnknownDerivedFromCkRecordIdInSource(object derivedCkRecordId, object ckRecordId) =>
        GetMessage("UnknownDerivedFromCkRecordIdInSource", derivedCkRecordId, ckRecordId);

    internal static OperationMessage CkEnumIdContainsInvalidCharacters(object ckEnumId) =>
        GetMessage("CkEnumIdContainsInvalidCharacters", ckEnumId);

    internal static OperationMessage EnumIdNotUnique(object ckEnumId) =>
        GetMessage("EnumIdNotUnique", ckEnumId);

    internal static OperationMessage CkEnumIdUndefined(object ckAttributeId) =>
        GetMessage("CkEnumIdUndefined", ckAttributeId);

    internal static OperationMessage CkTypeIdUnknownTargetAttributeIdForAssociation(object originCkTypeId, object roleId, object targetCkAttributeId, object targetCkTypeId) =>
        GetMessage("CkTypeIdUnknownTargetAttributeIdForAssociation", originCkTypeId, roleId, targetCkAttributeId, targetCkTypeId);

    internal static OperationMessage CkTypeIdAssociationRoleIdUnknown(object ckTypeId, object ckAssociationId) =>
        GetMessage("CkTypeIdAssociationRoleIdUnknown", ckTypeId, ckAssociationId);

    internal static OperationMessage CkAssociationRoleAttributeNameNotUnique(object ckAssociationRole, object attributeName) =>
        GetMessage("CkAssociationRoleAttributeNameNotUnique", ckAssociationRole, attributeName);

    internal static OperationMessage CkAssociationRoleAttributeIdNotUnique(object ckAssociationRole, object ckAttributeId) =>
        GetMessage("CkAssociationRoleAttributeIdNotUnique", ckAssociationRole, ckAttributeId);

    internal static OperationMessage CkAttributeIdNotFoundAtType(object ckAttributeId, object ckTypeId) =>
        GetMessage("CkAttributeIdNotFoundAtType", ckAttributeId, ckTypeId);

    internal static OperationMessage CkAttributeIdNotFoundAtRecord(object ckAttributeId, object ckRecordId) =>
        GetMessage("CkAttributeIdNotFoundAtRecord", ckAttributeId, ckRecordId);

    private static readonly Dictionary<string, OperationMessageTemplate> Templates = new()
    {
        {
            "UnknownCkModel",
             new OperationMessageTemplate(MessageLevel.Error,
                 1, "Repository does not contain construction kit model '{modelId}'.",
                 new [] {"modelId"})
        },
        {
            "UnknownAttributeOfCkTypeIdInSource",
             new OperationMessageTemplate(MessageLevel.Error,
                 2, "Attribute Id '{ckAttributeId}' of CkTypeId '{ckTypeId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckAttributeId", "ckTypeId"})
        },
        {
            "UnknownCkDerivedIdOfCkTypeIdInSource",
             new OperationMessageTemplate(MessageLevel.Error,
                 3, "Derived CkTypeId '{derivedCkTypeId}' of CkTypeId '{ckTypeId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"derivedCkTypeId", "ckTypeId"})
        },
        {
            "UnknownAssociationRoleOfCkTypeIdInSource",
             new OperationMessageTemplate(MessageLevel.Error,
                 4, "CkTypeId '{ckTypeId}' defines unknown association role '{roleId}'. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckTypeId", "roleId"})
        },
        {
            "UnknownTargetCkTypeIdOfCkTypeIdInSource",
             new OperationMessageTemplate(MessageLevel.Error,
                 5, "CkTypeId '{ckTypeId}' defines unknown association role target CkTypeId '{targetCkTypeId}'. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckTypeId", "targetCkTypeId"})
        },
        {
            "AttributeIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 6, "AttributeId '{ckAttributeId}' is not unique.",
                 new [] {"ckAttributeId"})
        },
        {
            "AssociationRoleIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 7, "AssociationRoleId '{ckAssociationId}' is not unique.",
                 new [] {"ckAssociationId"})
        },
        {
            "TypeIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 8, "TypeId '{ckTypeId}' is not unique.",
                 new [] {"ckTypeId"})
        },
        {
            "InheritanceMissing",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 9, "TypeId '{ckTypeId}' has no inheritance definition. Ensure that attribute ckDerivedId is set.",
                 new [] {"ckTypeId"})
        },
        {
            "CircularDependency",
             new OperationMessageTemplate(MessageLevel.Error,
                 10, "ModelId '{modelId}' has defined a dependency to '{dependentModelId}' that results to a circular dependencies.",
                 new [] {"modelId", "dependentModelId"})
        },
        {
            "UnknownCkTypeIdForInheritance",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 11, "CkTypeId '{ckTypeId}' is unknown for inheritance. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckTypeId"})
        },
        {
            "CkTypeIdAttributeIdNotUniqueByInheritance",
             new OperationMessageTemplate(MessageLevel.Error,
                 12, "CkTypeId '{ckTypeId}' defines AttributeId '{ckAttributeId}' that violates at derived CkTypeId '{derivedCkTypeId}' the unique attribute id constraint.",
                 new [] {"ckTypeId", "ckAttributeId", "derivedCkTypeId"})
        },
        {
            "CkTypeIdAttributeNameNotUniqueByInheritance",
             new OperationMessageTemplate(MessageLevel.Error,
                 13, "CkTypeId '{ckTypeId}' defines attribute names '{attributeNames}' by inheritance that violates the unique attribute name constraint.",
                 new [] {"ckTypeId", "attributeNames"})
        },
        {
            "CkTypeIdAssociationNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 14, "CkTypeId '{ckTypeId}' defines AssociationRoleId '{ckAssociationId}' to CkTypeId '{targetCkTypeId}' that violates the unique association constraint",
                 new [] {"ckTypeId", "ckAssociationId", "targetCkTypeId"})
        },
        {
            "CkTypeIdAttributeNameNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 15, "CkTypeId '{ckTypeId}' defines attribute names '{attributeName}' that violates the unique attribute name constraint.",
                 new [] {"ckTypeId", "attributeName"})
        },
        {
            "CkTypeIdAttributeIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 16, "CkTypeId '{ckTypeId}' defines AttributeId(s) '{ckAttributeId}' that violates the unique attribute id constraint.",
                 new [] {"ckTypeId", "ckAttributeId"})
        },
        {
            "CkTypeIdOutAssociationNotUniqueByInheritance",
             new OperationMessageTemplate(MessageLevel.Error,
                 17, "CkTypeId '{ckTypeId}' defines an outgoing AssociationRoleId '{ckAssociationId}' to CkTypeId '{targetCkTypeId}' by inheritance that violates the unique association role id constraint",
                 new [] {"ckTypeId", "ckAssociationId", "targetCkTypeId"})
        },
        {
            "CkTypeIdUnknownTargetCkTypeIdForAssociation",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 18, "CkTypeId '{originCkTypeId}' defines a unknown target CkTypeId '{targetCkTypeId}' for role id '{roleId}'. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"originCkTypeId", "targetCkTypeId", "roleId"})
        },
        {
            "CkTypeIdUnknown",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 19, "CkTypeId '{ckTypeId}' is unknown. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckTypeId"})
        },
        {
            "CkTypeIdMultipleOutgoingAssociationRepresentingSameRole",
             new OperationMessageTemplate(MessageLevel.Error,
                 20, "CkTypeId '{ckTypeId}' defines an outgoing AssociationRoleId '{ckAssociationId}' to CkTypeId '{targetCkTypeId}'. This association is also defined between CkTypeId '{otherCkTypeId}' and target CkTypeId '{otherTargetCkTypeId}'.",
                 new [] {"ckTypeId", "ckAssociationId", "targetCkTypeId", "otherCkTypeId", "otherTargetCkTypeId"})
        },
        {
            "DerivedFromCkTypeIdThatIsFinal",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 21, "CkTypeId '{baseCkTypeId}' is final, but CkTypeId '{derivedTypeId}' is derived from it.",
                 new [] {"baseCkTypeId", "derivedTypeId"})
        },
        {
            "DirectoryMustBeEmpty",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 22, "Directory '{directory}' must be empty.",
                 new [] {"directory"})
        },
        {
            "ModelIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 23, "ModelId '{modelId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"modelId"})
        },
        {
            "CkTypeIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.Error,
                 24, "CkTypeId '{ckTypeId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckTypeId"})
        },
        {
            "CkAttributeIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.Error,
                 25, "CkAttributeId '{ckAttributeId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckAttributeId"})
        },
        {
            "CkAssociationIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.Error,
                 26, "CkAssociationId '{ckAssociationId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckAssociationId"})
        },
        {
            "SchemaValidationError",
             new OperationMessageTemplate(MessageLevel.Error,
                 27, "{locationReference}: Schema validation failed: '{errorMessage}'",
                 new [] {"locationReference", "errorMessage"})
        },
        {
            "DirectoryDoesNotExist",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 28, "Directory '{directoryPath}' does not exist.",
                 new [] {"directoryPath"})
        },
        {
            "FileDoesNotExist",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 29, "File '{filePath}' does not exist.",
                 new [] {"filePath"})
        },
        {
            "SelectionValueNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 30, "CkEnumId '{ckEnumId}' has defined key '{key}' which is used several times.",
                 new [] {"ckEnumId", "key"})
        },
        {
            "CkRecordIdUndefined",
             new OperationMessageTemplate(MessageLevel.Error,
                 31, "CkAttributeId '{ckAttributeId}' is defined as Record, but the ValueCkRecordId is missing.",
                 new [] {"ckAttributeId"})
        },
        {
            "CkRecordIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.Error,
                 32, "CkRecordId '{ckRecordId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckRecordId"})
        },
        {
            "RecordIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 33, "RecordId '{ckRecordId}' is not unique.",
                 new [] {"ckRecordId"})
        },
        {
            "CkRecordIdUnknown",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 34, "CkRecordId '{ckRecordId}' is unknown. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckRecordId"})
        },
        {
            "UnknownCkRecordIdForInheritance",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 35, "CkRecordId '{ckRecordId}' is unknown for inheritance. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckRecordId"})
        },
        {
            "DerivedFromCkRecordIdThatIsFinal",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 36, "CkRecordId '{baseCkRecordId}' is final, but CkRecordId '{derivedCkRecordId}' is derived from it.",
                 new [] {"baseCkRecordId", "derivedCkRecordId"})
        },
        {
            "CkRecordIdAttributeNameNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 37, "CkRecordId '{ckRecordId}' defines attribute name '{attributeName}' that violates the unique attribute name constraint.",
                 new [] {"ckRecordId", "attributeName"})
        },
        {
            "CkRecordIdAttributeIdNotUniqueByInheritance",
             new OperationMessageTemplate(MessageLevel.Error,
                 38, "CkRecordId '{ckRecordId}' defines AttributeId '{ckAttributeId}' that violates at derived CkRecordId '{derivedCkRecordId}' the unique attribute id constraint.",
                 new [] {"ckRecordId", "ckAttributeId", "derivedCkRecordId"})
        },
        {
            "CkRecordIdAttributeIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 39, "CkRecordId '{ckRecordId}' defines AttributeIds '{ckAttributeId}' that violates the unique attribute id constraint.",
                 new [] {"ckRecordId", "ckAttributeId"})
        },
        {
            "CkRecordIdAttributeNameNotUniqueByInheritance",
             new OperationMessageTemplate(MessageLevel.Error,
                 40, "CkRecordId '{ckRecordId}' defines attribute name '{attributeNames}' by inheritance that violates the unique attribute name constraint.",
                 new [] {"ckRecordId", "attributeNames"})
        },
        {
            "AttributeUsesUnknownCkRecordId",
             new OperationMessageTemplate(MessageLevel.Error,
                 41, "CkAttributeId '{ckAttributeId}' uses unknown CkRecordId '{ckRecordId}'. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckAttributeId", "ckRecordId"})
        },
        {
            "UnknownAttributeOfCkRecordIdInSource",
             new OperationMessageTemplate(MessageLevel.Error,
                 42, "Attribute Id '{ckAttributeId}' of CkRecordId '{ckRecordId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckAttributeId", "ckRecordId"})
        },
        {
            "UnknownDerivedFromCkRecordIdInSource",
             new OperationMessageTemplate(MessageLevel.Error,
                 43, "Derived CkRecordId '{derivedCkRecordId}' of CkRecordId '{ckRecordId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"derivedCkRecordId", "ckRecordId"})
        },
        {
            "CkEnumIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.Error,
                 44, "CkEnumId '{ckEnumId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckEnumId"})
        },
        {
            "EnumIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 45, "CkEnumId '{ckEnumId}' is not unique.",
                 new [] {"ckEnumId"})
        },
        {
            "CkEnumIdUndefined",
             new OperationMessageTemplate(MessageLevel.Error,
                 46, "CkAttributeId '{ckAttributeId}' is defined as Enum, but the ValueCkEnumId is missing.",
                 new [] {"ckAttributeId"})
        },
        {
            "CkTypeIdUnknownTargetAttributeIdForAssociation",
             new OperationMessageTemplate(MessageLevel.Error,
                 47, "CkTypeId '{originCkTypeId}' defines for role id '{roleId}' an unknown target AttributeId '{targetCkAttributeId}' for CkType '{targetCkTypeId}'.",
                 new [] {"originCkTypeId", "roleId", "targetCkAttributeId", "targetCkTypeId"})
        },
        {
            "CkTypeIdAssociationRoleIdUnknown",
             new OperationMessageTemplate(MessageLevel.Error,
                 48, "CkTypeId '{ckTypeId}' defines AssociationRoleId '{ckAssociationId}' that is unknown. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckTypeId", "ckAssociationId"})
        },
        {
            "CkAssociationRoleAttributeNameNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 49, "CkAssociationRole '{ckAssociationRole}' defines attribute name '{attributeName}' that violates the unique attribute name constraint.",
                 new [] {"ckAssociationRole", "attributeName"})
        },
        {
            "CkAssociationRoleAttributeIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 50, "CkAssociationRole '{ckAssociationRole}' defines AttributeIds '{ckAttributeId}' that violates the unique attribute id constraint.",
                 new [] {"ckAssociationRole", "ckAttributeId"})
        },
        {
            "CkAttributeIdNotFoundAtType",
             new OperationMessageTemplate(MessageLevel.Error,
                 51, "CkAttributeId '{ckAttributeId}' defined at type '{ckTypeId}' not found.",
                 new [] {"ckAttributeId", "ckTypeId"})
        },
        {
            "CkAttributeIdNotFoundAtRecord",
             new OperationMessageTemplate(MessageLevel.Error,
                 52, "CkAttributeId '{ckAttributeId}' defined at record '{ckRecordId}' not found.",
                 new [] {"ckAttributeId", "ckRecordId"})
        },
    };
}

