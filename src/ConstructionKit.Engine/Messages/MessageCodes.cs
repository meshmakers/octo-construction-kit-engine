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
    internal static OperationMessage GetMessage(string messageKey, string? location, params object[] args)
    {
        if (!Templates.ContainsKey(messageKey))
        {
            throw new ArgumentOutOfRangeException($"Message with key '{messageKey}' does not exist.");
        }
        return Templates[messageKey].CreateMessage(location, args);
    }

    internal static OperationMessage UnknownCkModel(string? location, object modelId) =>
        GetMessage("UnknownCkModel", location, modelId);

    internal static OperationMessage UnknownAttributeOfCkTypeIdInSource(string? location, object ckAttributeId, object ckTypeId) =>
        GetMessage("UnknownAttributeOfCkTypeIdInSource", location, ckAttributeId, ckTypeId);

    internal static OperationMessage UnknownCkDerivedIdOfCkTypeIdInSource(string? location, object derivedCkTypeId, object ckTypeId) =>
        GetMessage("UnknownCkDerivedIdOfCkTypeIdInSource", location, derivedCkTypeId, ckTypeId);

    internal static OperationMessage UnknownAssociationRoleOfCkTypeIdInSource(string? location, object ckTypeId, object roleId) =>
        GetMessage("UnknownAssociationRoleOfCkTypeIdInSource", location, ckTypeId, roleId);

    internal static OperationMessage UnknownTargetCkTypeIdOfCkTypeIdInSource(string? location, object ckTypeId, object targetCkTypeId) =>
        GetMessage("UnknownTargetCkTypeIdOfCkTypeIdInSource", location, ckTypeId, targetCkTypeId);

    internal static OperationMessage AttributeIdNotUnique(string? location, object ckAttributeId) =>
        GetMessage("AttributeIdNotUnique", location, ckAttributeId);

    internal static OperationMessage AssociationRoleIdNotUnique(string? location, object ckAssociationId) =>
        GetMessage("AssociationRoleIdNotUnique", location, ckAssociationId);

    internal static OperationMessage TypeIdNotUnique(string? location, object ckTypeId) =>
        GetMessage("TypeIdNotUnique", location, ckTypeId);

    internal static OperationMessage InheritanceMissing(string? location, object ckTypeId) =>
        GetMessage("InheritanceMissing", location, ckTypeId);

    internal static OperationMessage CircularDependency(string? location, object modelId, object dependentModelId) =>
        GetMessage("CircularDependency", location, modelId, dependentModelId);

    internal static OperationMessage UnknownCkTypeIdForInheritance(string? location, object ckTypeId) =>
        GetMessage("UnknownCkTypeIdForInheritance", location, ckTypeId);

    internal static OperationMessage CkTypeIdAttributeIdNotUniqueByInheritance(string? location, object ckTypeId, object ckAttributeId, object derivedCkTypeId) =>
        GetMessage("CkTypeIdAttributeIdNotUniqueByInheritance", location, ckTypeId, ckAttributeId, derivedCkTypeId);

    internal static OperationMessage CkTypeIdAttributeNameNotUniqueByInheritance(string? location, object ckTypeId, object attributeNames) =>
        GetMessage("CkTypeIdAttributeNameNotUniqueByInheritance", location, ckTypeId, attributeNames);

    internal static OperationMessage CkTypeIdAssociationNotUnique(string? location, object ckTypeId, object ckAssociationId, object targetCkTypeId) =>
        GetMessage("CkTypeIdAssociationNotUnique", location, ckTypeId, ckAssociationId, targetCkTypeId);

    internal static OperationMessage CkTypeIdAttributeNameNotUnique(string? location, object ckTypeId, object attributeName) =>
        GetMessage("CkTypeIdAttributeNameNotUnique", location, ckTypeId, attributeName);

    internal static OperationMessage CkTypeIdAttributeIdNotUnique(string? location, object ckTypeId, object ckAttributeId) =>
        GetMessage("CkTypeIdAttributeIdNotUnique", location, ckTypeId, ckAttributeId);

    internal static OperationMessage CkTypeIdOutAssociationNotUniqueByInheritance(string? location, object ckTypeId, object ckAssociationId, object targetCkTypeId) =>
        GetMessage("CkTypeIdOutAssociationNotUniqueByInheritance", location, ckTypeId, ckAssociationId, targetCkTypeId);

    internal static OperationMessage CkTypeIdUnknownTargetCkTypeIdForAssociation(string? location, object originCkTypeId, object targetCkTypeId, object roleId) =>
        GetMessage("CkTypeIdUnknownTargetCkTypeIdForAssociation", location, originCkTypeId, targetCkTypeId, roleId);

    internal static OperationMessage CkTypeIdUnknown(string? location, object ckTypeId) =>
        GetMessage("CkTypeIdUnknown", location, ckTypeId);

    internal static OperationMessage CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(string? location, object ckTypeId, object ckAssociationId, object targetCkTypeId, object otherCkTypeId, object otherTargetCkTypeId) =>
        GetMessage("CkTypeIdMultipleOutgoingAssociationRepresentingSameRole", location, ckTypeId, ckAssociationId, targetCkTypeId, otherCkTypeId, otherTargetCkTypeId);

    internal static OperationMessage DerivedFromCkTypeIdThatIsFinal(string? location, object baseCkTypeId, object derivedTypeId) =>
        GetMessage("DerivedFromCkTypeIdThatIsFinal", location, baseCkTypeId, derivedTypeId);

    internal static OperationMessage DirectoryMustBeEmpty(string? location) =>
        GetMessage("DirectoryMustBeEmpty", location);
    internal static OperationMessage ModelIdContainsInvalidCharacters(string? location, object modelId) =>
        GetMessage("ModelIdContainsInvalidCharacters", location, modelId);

    internal static OperationMessage CkTypeIdContainsInvalidCharacters(string? location, object ckTypeId) =>
        GetMessage("CkTypeIdContainsInvalidCharacters", location, ckTypeId);

    internal static OperationMessage CkAttributeIdContainsInvalidCharacters(string? location, object ckAttributeId) =>
        GetMessage("CkAttributeIdContainsInvalidCharacters", location, ckAttributeId);

    internal static OperationMessage CkAssociationIdContainsInvalidCharacters(string? location, object ckAssociationId) =>
        GetMessage("CkAssociationIdContainsInvalidCharacters", location, ckAssociationId);

    internal static OperationMessage SchemaValidationError(string? location, object path, object errorMessage) =>
        GetMessage("SchemaValidationError", location, path, errorMessage);

    internal static OperationMessage DirectoryDoesNotExist(string? location) =>
        GetMessage("DirectoryDoesNotExist", location);
    internal static OperationMessage FileDoesNotExist(string? location) =>
        GetMessage("FileDoesNotExist", location);
    internal static OperationMessage SelectionValueNotUnique(string? location, object ckEnumId, object key) =>
        GetMessage("SelectionValueNotUnique", location, ckEnumId, key);

    internal static OperationMessage CkRecordIdUndefined(string? location, object ckAttributeId) =>
        GetMessage("CkRecordIdUndefined", location, ckAttributeId);

    internal static OperationMessage CkRecordIdContainsInvalidCharacters(string? location, object ckRecordId) =>
        GetMessage("CkRecordIdContainsInvalidCharacters", location, ckRecordId);

    internal static OperationMessage RecordIdNotUnique(string? location, object ckRecordId) =>
        GetMessage("RecordIdNotUnique", location, ckRecordId);

    internal static OperationMessage CkRecordIdUnknown(string? location, object ckRecordId) =>
        GetMessage("CkRecordIdUnknown", location, ckRecordId);

    internal static OperationMessage UnknownCkRecordIdForInheritance(string? location, object ckRecordId) =>
        GetMessage("UnknownCkRecordIdForInheritance", location, ckRecordId);

    internal static OperationMessage DerivedFromCkRecordIdThatIsFinal(string? location, object baseCkRecordId, object derivedCkRecordId) =>
        GetMessage("DerivedFromCkRecordIdThatIsFinal", location, baseCkRecordId, derivedCkRecordId);

    internal static OperationMessage CkRecordIdAttributeNameNotUnique(string? location, object ckRecordId, object attributeName) =>
        GetMessage("CkRecordIdAttributeNameNotUnique", location, ckRecordId, attributeName);

    internal static OperationMessage CkRecordIdAttributeIdNotUniqueByInheritance(string? location, object ckRecordId, object ckAttributeId, object derivedCkRecordId) =>
        GetMessage("CkRecordIdAttributeIdNotUniqueByInheritance", location, ckRecordId, ckAttributeId, derivedCkRecordId);

    internal static OperationMessage CkRecordIdAttributeIdNotUnique(string? location, object ckRecordId, object ckAttributeId) =>
        GetMessage("CkRecordIdAttributeIdNotUnique", location, ckRecordId, ckAttributeId);

    internal static OperationMessage CkRecordIdAttributeNameNotUniqueByInheritance(string? location, object ckRecordId, object attributeNames) =>
        GetMessage("CkRecordIdAttributeNameNotUniqueByInheritance", location, ckRecordId, attributeNames);

    internal static OperationMessage AttributeUsesUnknownCkRecordId(string? location, object ckAttributeId, object ckRecordId) =>
        GetMessage("AttributeUsesUnknownCkRecordId", location, ckAttributeId, ckRecordId);

    internal static OperationMessage UnknownAttributeOfCkRecordIdInSource(string? location, object ckAttributeId, object ckRecordId) =>
        GetMessage("UnknownAttributeOfCkRecordIdInSource", location, ckAttributeId, ckRecordId);

    internal static OperationMessage UnknownDerivedFromCkRecordIdInSource(string? location, object derivedCkRecordId, object ckRecordId) =>
        GetMessage("UnknownDerivedFromCkRecordIdInSource", location, derivedCkRecordId, ckRecordId);

    internal static OperationMessage CkEnumIdContainsInvalidCharacters(string? location, object ckEnumId) =>
        GetMessage("CkEnumIdContainsInvalidCharacters", location, ckEnumId);

    internal static OperationMessage EnumIdNotUnique(string? location, object ckEnumId) =>
        GetMessage("EnumIdNotUnique", location, ckEnumId);

    internal static OperationMessage CkEnumIdUndefined(string? location, object ckAttributeId) =>
        GetMessage("CkEnumIdUndefined", location, ckAttributeId);

    internal static OperationMessage CkTypeIdUnknownTargetAttributeIdForAssociation(string? location, object originCkTypeId, object roleId, object targetCkAttributeId, object targetCkTypeId) =>
        GetMessage("CkTypeIdUnknownTargetAttributeIdForAssociation", location, originCkTypeId, roleId, targetCkAttributeId, targetCkTypeId);

    internal static OperationMessage CkTypeIdAssociationRoleIdUnknown(string? location, object ckTypeId, object ckAssociationId) =>
        GetMessage("CkTypeIdAssociationRoleIdUnknown", location, ckTypeId, ckAssociationId);

    internal static OperationMessage CkAssociationRoleAttributeNameNotUnique(string? location, object ckAssociationRole, object attributeName) =>
        GetMessage("CkAssociationRoleAttributeNameNotUnique", location, ckAssociationRole, attributeName);

    internal static OperationMessage CkAssociationRoleAttributeIdNotUnique(string? location, object ckAssociationRole, object ckAttributeId) =>
        GetMessage("CkAssociationRoleAttributeIdNotUnique", location, ckAssociationRole, ckAttributeId);

    internal static OperationMessage CkAttributeIdNotFoundAtType(string? location, object ckAttributeId, object ckTypeId) =>
        GetMessage("CkAttributeIdNotFoundAtType", location, ckAttributeId, ckTypeId);

    internal static OperationMessage CkAttributeIdNotFoundAtRecord(string? location, object ckAttributeId, object ckRecordId) =>
        GetMessage("CkAttributeIdNotFoundAtRecord", location, ckAttributeId, ckRecordId);

    internal static OperationMessage FileContainsNoModel(string? location) =>
        GetMessage("FileContainsNoModel", location);
    internal static OperationMessage NoImportsFound(string? location) =>
        GetMessage("NoImportsFound", location);
    internal static OperationMessage EnumIsNotExtensibleButContainsExtension(string? location, object ckEnumId) =>
        GetMessage("EnumIsNotExtensibleButContainsExtension", location, ckEnumId);

    internal static OperationMessage EnumNameMayNotContainWhitespaceSpecialCharacters(string? location, object ckEnumId, object CKEnumKey) =>
        GetMessage("EnumNameMayNotContainWhitespaceSpecialCharacters", location, ckEnumId, CKEnumKey);

    internal static OperationMessage EnumNameMyNotBeEmpty(string? location, object ckEnumId, object CKEnumKey) =>
        GetMessage("EnumNameMyNotBeEmpty", location, ckEnumId, CKEnumKey);

    internal static OperationMessage EnumKeyMayNotBeNegative(string? location, object ckEnumId, object CKEnumKey) =>
        GetMessage("EnumKeyMayNotBeNegative", location, ckEnumId, CKEnumKey);

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
                 2, "CkAttributeId '{ckAttributeId}' of CkTypeId '{ckTypeId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
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
                 6, "CkAttributeId '{ckAttributeId}' is not unique.",
                 new [] {"ckAttributeId"})
        },
        {
            "AssociationRoleIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 7, "CkAssociationRoleId '{ckAssociationId}' is not unique.",
                 new [] {"ckAssociationId"})
        },
        {
            "TypeIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 8, "CkTypeId '{ckTypeId}' is not unique.",
                 new [] {"ckTypeId"})
        },
        {
            "InheritanceMissing",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 9, "CkTypeId '{ckTypeId}' has no inheritance definition. Ensure that attribute ckDerivedId is set.",
                 new [] {"ckTypeId"})
        },
        {
            "CircularDependency",
             new OperationMessageTemplate(MessageLevel.Error,
                 10, "CkModelId '{modelId}' has defined a dependency to '{dependentModelId}' that results to a circular dependencies.",
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
                 22, "Directory must be empty.",
                 new string[] {})
        },
        {
            "ModelIdContainsInvalidCharacters",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 23, "CkModelId '{modelId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
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
                 27, "Schema validation failed at '{path}'->'{errorMessage}'",
                 new [] {"path", "errorMessage"})
        },
        {
            "DirectoryDoesNotExist",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 28, "Directory does not exist.",
                 new string[] {})
        },
        {
            "FileDoesNotExist",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 29, "File does not exist.",
                 new string[] {})
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
        {
            "FileContainsNoModel",
             new OperationMessageTemplate(MessageLevel.Warning,
                 53, "File does not contain a model. It will be ignored.",
                 new string[] {})
        },
        {
            "NoImportsFound",
             new OperationMessageTemplate(MessageLevel.Warning,
                 54, "No imports founds in construction kit model configuration file.",
                 new string[] {})
        },
        {
            "EnumIsNotExtensibleButContainsExtension",
             new OperationMessageTemplate(MessageLevel.Error,
                 55, "Enum '{ckEnumId}' is not extensible but contains an extension.",
                 new [] {"ckEnumId"})
        },
        {
            "EnumNameMayNotContainWhitespaceSpecialCharacters",
             new OperationMessageTemplate(MessageLevel.Error,
                 56, "Enum '{ckEnumId}', key '{CKEnumKey}' name may not contain whitespace or special characters.",
                 new [] {"ckEnumId", "CKEnumKey"})
        },
        {
            "EnumNameMyNotBeEmpty",
             new OperationMessageTemplate(MessageLevel.Error,
                 57, "Enum '{ckEnumId}', key '{CKEnumKey}' name may not contain whitespace or special characters.",
                 new [] {"ckEnumId", "CKEnumKey"})
        },
        {
            "EnumKeyMayNotBeNegative",
             new OperationMessageTemplate(MessageLevel.Error,
                 58, "Enum '{ckEnumId}', key '{CKEnumKey}' cannot be negative.",
                 new [] {"ckEnumId", "CKEnumKey"})
        },
    };
}

