
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
    internal static CompilerMessage GetMessage(string messageKey, params object[] args)
    {
        if (!Templates.ContainsKey(messageKey))
        {
            throw new ArgumentOutOfRangeException($"Message with key '{messageKey}' does not exist.");
        }
        return Templates[messageKey].CreateMessage(args);
    }

    internal static CompilerMessage UnknownCkModel(object modelId) =>
        GetMessage("UnknownCkModel", modelId);

    internal static CompilerMessage UnknownAttributeOfCkTypeIdInSource(object ckAttributeId, object ckTypeId) =>
        GetMessage("UnknownAttributeOfCkTypeIdInSource", ckAttributeId, ckTypeId);

    internal static CompilerMessage UnknownCkDerivedIdOfCkTypeIdInSource(object derivedCkTypeId, object ckTypeId) =>
        GetMessage("UnknownCkDerivedIdOfCkTypeIdInSource", derivedCkTypeId, ckTypeId);

    internal static CompilerMessage UnknownAssociationRoleOfCkTypeIdInSource(object ckTypeId, object roleId) =>
        GetMessage("UnknownAssociationRoleOfCkTypeIdInSource", ckTypeId, roleId);

    internal static CompilerMessage UnknownTargetCkTypeIdOfCkTypeIdInSource(object ckTypeId, object targetCkTypeId) =>
        GetMessage("UnknownTargetCkTypeIdOfCkTypeIdInSource", ckTypeId, targetCkTypeId);

    internal static CompilerMessage AttributeIdNotUnique(object ckAttributeId) =>
        GetMessage("AttributeIdNotUnique", ckAttributeId);

    internal static CompilerMessage AssociationRoleIdNotUnique(object ckAssociationId) =>
        GetMessage("AssociationRoleIdNotUnique", ckAssociationId);

    internal static CompilerMessage TypeIdNotUnique(object ckTypeId) =>
        GetMessage("TypeIdNotUnique", ckTypeId);

    internal static CompilerMessage InheritanceMissing(object ckTypeId) =>
        GetMessage("InheritanceMissing", ckTypeId);

    internal static CompilerMessage CircularDependency(object modelId, object dependentModelId) =>
        GetMessage("CircularDependency", modelId, dependentModelId);

    internal static CompilerMessage UnknownCkTypeIdForInheritance(object ckTypeId) =>
        GetMessage("UnknownCkTypeIdForInheritance", ckTypeId);

    internal static CompilerMessage CkTypeIdAttributeIdNotUniqueByInheritance(object ckTypeId, object ckAttributeId) =>
        GetMessage("CkTypeIdAttributeIdNotUniqueByInheritance", ckTypeId, ckAttributeId);

    internal static CompilerMessage CkTypeIdAttributeNameNotUniqueByInheritance(object ckTypeId, object attributeName) =>
        GetMessage("CkTypeIdAttributeNameNotUniqueByInheritance", ckTypeId, attributeName);

    internal static CompilerMessage CkTypeIdAssociationNotUnique(object ckTypeId, object ckAssociationId, object targetCkTypeId) =>
        GetMessage("CkTypeIdAssociationNotUnique", ckTypeId, ckAssociationId, targetCkTypeId);

    internal static CompilerMessage CkTypeIdAttributeNameNotUnique(object ckTypeId, object attributeName) =>
        GetMessage("CkTypeIdAttributeNameNotUnique", ckTypeId, attributeName);

    internal static CompilerMessage CkTypeIdAttributeIdNotUnique(object ckTypeId, object ckAttributeId) =>
        GetMessage("CkTypeIdAttributeIdNotUnique", ckTypeId, ckAttributeId);

    internal static CompilerMessage CkTypeIdOutAssociationNotUniqueByInheritance(object ckTypeId, object ckAssociationId, object targetCkTypeId) =>
        GetMessage("CkTypeIdOutAssociationNotUniqueByInheritance", ckTypeId, ckAssociationId, targetCkTypeId);

    internal static CompilerMessage CkTypeIdUnknownTargetCkTypeIdForAssociation(object originCkTypeId, object targetCkTypeId, object roleId) =>
        GetMessage("CkTypeIdUnknownTargetCkTypeIdForAssociation", originCkTypeId, targetCkTypeId, roleId);

    internal static CompilerMessage CkTypeIdUnknown(object ckTypeId) =>
        GetMessage("CkTypeIdUnknown", ckTypeId);

    internal static CompilerMessage CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(object ckTypeId, object ckAssociationId, object targetCkTypeId, object otherCkTypeId, object otherTargetCkTypeId) =>
        GetMessage("CkTypeIdMultipleOutgoingAssociationRepresentingSameRole", ckTypeId, ckAssociationId, targetCkTypeId, otherCkTypeId, otherTargetCkTypeId);

    internal static CompilerMessage DerivedFromCkTypeIdThatIsFinal(object baseCkTypeId, object derivedTypeId) =>
        GetMessage("DerivedFromCkTypeIdThatIsFinal", baseCkTypeId, derivedTypeId);

    internal static CompilerMessage DirectoryMustBeEmpty(object directory) =>
        GetMessage("DirectoryMustBeEmpty", directory);

    internal static CompilerMessage ModelIdContainsInvalidCharacters(object modelId) =>
        GetMessage("ModelIdContainsInvalidCharacters", modelId);

    internal static CompilerMessage CkTypeIdContainsInvalidCharacters(object ckTypeId) =>
        GetMessage("CkTypeIdContainsInvalidCharacters", ckTypeId);

    internal static CompilerMessage CkAttributeIdContainsInvalidCharacters(object ckAttributeId) =>
        GetMessage("CkAttributeIdContainsInvalidCharacters", ckAttributeId);

    internal static CompilerMessage CkAssociationIdContainsInvalidCharacters(object ckAssociationId) =>
        GetMessage("CkAssociationIdContainsInvalidCharacters", ckAssociationId);

    internal static CompilerMessage SchemaValidationError(object errorMessage) =>
        GetMessage("SchemaValidationError", errorMessage);

    internal static CompilerMessage DirectoryDoesNotExist(object directoryPath) =>
        GetMessage("DirectoryDoesNotExist", directoryPath);

    internal static CompilerMessage FileDoesNotExist(object filePath) =>
        GetMessage("FileDoesNotExist", filePath);

    internal static CompilerMessage SelectionValueNotUnique(object ckEnumId, object key) =>
        GetMessage("SelectionValueNotUnique", ckEnumId, key);

    internal static CompilerMessage CkRecordIdUndefined(object ckAttributeId) =>
        GetMessage("CkRecordIdUndefined", ckAttributeId);

    internal static CompilerMessage CkRecordIdContainsInvalidCharacters(object ckRecordId) =>
        GetMessage("CkRecordIdContainsInvalidCharacters", ckRecordId);

    internal static CompilerMessage RecordIdNotUnique(object ckRecordId) =>
        GetMessage("RecordIdNotUnique", ckRecordId);

    internal static CompilerMessage CkRecordIdUnknown(object ckRecordId) =>
        GetMessage("CkRecordIdUnknown", ckRecordId);

    internal static CompilerMessage UnknownCkRecordIdForInheritance(object ckRecordId) =>
        GetMessage("UnknownCkRecordIdForInheritance", ckRecordId);

    internal static CompilerMessage DerivedFromCkRecordIdThatIsFinal(object baseCkRecordId, object derivedCkRecordId) =>
        GetMessage("DerivedFromCkRecordIdThatIsFinal", baseCkRecordId, derivedCkRecordId);

    internal static CompilerMessage CkRecordIdAttributeNameNotUnique(object ckRecordId, object attributeName) =>
        GetMessage("CkRecordIdAttributeNameNotUnique", ckRecordId, attributeName);

    internal static CompilerMessage CkRecordIdAttributeIdNotUniqueByInheritance(object ckRecordId, object ckAttributeId) =>
        GetMessage("CkRecordIdAttributeIdNotUniqueByInheritance", ckRecordId, ckAttributeId);

    internal static CompilerMessage CkRecordIdAttributeIdNotUnique(object ckRecordId, object ckAttributeId) =>
        GetMessage("CkRecordIdAttributeIdNotUnique", ckRecordId, ckAttributeId);

    internal static CompilerMessage CkRecordIdAttributeNameNotUniqueByInheritance(object ckRecordId, object attributeName) =>
        GetMessage("CkRecordIdAttributeNameNotUniqueByInheritance", ckRecordId, attributeName);

    internal static CompilerMessage AttributeUsesUnknownCkRecordId(object ckAttributeId, object ckRecordId) =>
        GetMessage("AttributeUsesUnknownCkRecordId", ckAttributeId, ckRecordId);

    internal static CompilerMessage UnknownAttributeOfCkRecordIdInSource(object ckAttributeId, object ckRecordId) =>
        GetMessage("UnknownAttributeOfCkRecordIdInSource", ckAttributeId, ckRecordId);

    internal static CompilerMessage UnknownDerivedFromCkRecordIdInSource(object derivedCkRecordId, object ckRecordId) =>
        GetMessage("UnknownDerivedFromCkRecordIdInSource", derivedCkRecordId, ckRecordId);

    internal static CompilerMessage CkEnumIdContainsInvalidCharacters(object ckEnumId) =>
        GetMessage("CkEnumIdContainsInvalidCharacters", ckEnumId);

    internal static CompilerMessage EnumIdNotUnique(object ckEnumId) =>
        GetMessage("EnumIdNotUnique", ckEnumId);

    internal static CompilerMessage CkEnumIdUndefined(object ckAttributeId) =>
        GetMessage("CkEnumIdUndefined", ckAttributeId);

    internal static CompilerMessage CkTypeIdUnknownTargetAttributeIdForAssociation(object originCkTypeId, object roleId, object targetCkAttributeId, object targetCkTypeId) =>
        GetMessage("CkTypeIdUnknownTargetAttributeIdForAssociation", originCkTypeId, roleId, targetCkAttributeId, targetCkTypeId);

    private static readonly Dictionary<string, CompilerMessageTemplate> Templates = new()
    {
        {
            "UnknownCkModel",
             new CompilerMessageTemplate(MessageLevel.Error,
                 1, "Repository does not contain construction kit model '{modelId}'.",
                 new [] {"modelId"})
        },
        {
            "UnknownAttributeOfCkTypeIdInSource",
             new CompilerMessageTemplate(MessageLevel.Error,
                 2, "Attribute Id '{ckAttributeId}' of CkTypeId '{ckTypeId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckAttributeId", "ckTypeId"})
        },
        {
            "UnknownCkDerivedIdOfCkTypeIdInSource",
             new CompilerMessageTemplate(MessageLevel.Error,
                 3, "Derived CkTypeId '{derivedCkTypeId}' of CkTypeId '{ckTypeId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"derivedCkTypeId", "ckTypeId"})
        },
        {
            "UnknownAssociationRoleOfCkTypeIdInSource",
             new CompilerMessageTemplate(MessageLevel.Error,
                 4, "CkTypeId '{ckTypeId}' defines unknown association role '{roleId}'. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckTypeId", "roleId"})
        },
        {
            "UnknownTargetCkTypeIdOfCkTypeIdInSource",
             new CompilerMessageTemplate(MessageLevel.Error,
                 5, "CkTypeId '{ckTypeId}' defines unknown association role target CkTypeId '{targetCkTypeId}'. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckTypeId", "targetCkTypeId"})
        },
        {
            "AttributeIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 6, "AttributeId '{ckAttributeId}' is not unique.",
                 new [] {"ckAttributeId"})
        },
        {
            "AssociationRoleIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 7, "AssociationRoleId '{ckAssociationId}' is not unique.",
                 new [] {"ckAssociationId"})
        },
        {
            "TypeIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 8, "TypeId '{ckTypeId}' is not unique.",
                 new [] {"ckTypeId"})
        },
        {
            "InheritanceMissing",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 9, "TypeId '{ckTypeId}' has no inheritance definition. Ensure that attribute ckDerivedId is set.",
                 new [] {"ckTypeId"})
        },
        {
            "CircularDependency",
             new CompilerMessageTemplate(MessageLevel.Error,
                 10, "ModelId '{modelId}' has defined a dependency to '{dependentModelId}' that results to a circular dependencies.",
                 new [] {"modelId", "dependentModelId"})
        },
        {
            "UnknownCkTypeIdForInheritance",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 11, "CkTypeId '{ckTypeId}' is unknown for inheritance. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckTypeId"})
        },
        {
            "CkTypeIdAttributeIdNotUniqueByInheritance",
             new CompilerMessageTemplate(MessageLevel.Error,
                 12, "CkTypeId '{ckTypeId}' defines AttributeId '{ckAttributeId}' by inheritance that violates the unique attribute id constraint.",
                 new [] {"ckTypeId", "ckAttributeId"})
        },
        {
            "CkTypeIdAttributeNameNotUniqueByInheritance",
             new CompilerMessageTemplate(MessageLevel.Error,
                 13, "CkTypeId '{ckTypeId}' defines attribute name '{attributeName}' by inheritance that violates the unique attribute name constraint.",
                 new [] {"ckTypeId", "attributeName"})
        },
        {
            "CkTypeIdAssociationNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 14, "CkTypeId '{ckTypeId}' defines AssociationRoleId '{ckAssociationId}' to CkTypeId '{targetCkTypeId}' that violates the unique association constraint",
                 new [] {"ckTypeId", "ckAssociationId", "targetCkTypeId"})
        },
        {
            "CkTypeIdAttributeNameNotUnique",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 15, "CkTypeId '{ckTypeId}' defines attribute name '{attributeName}' that violates the unique attribute name constraint.",
                 new [] {"ckTypeId", "attributeName"})
        },
        {
            "CkTypeIdAttributeIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 16, "CkTypeId '{ckTypeId}' defines AttributeId '{ckAttributeId}' that violates the unique attribute id constraint.",
                 new [] {"ckTypeId", "ckAttributeId"})
        },
        {
            "CkTypeIdOutAssociationNotUniqueByInheritance",
             new CompilerMessageTemplate(MessageLevel.Error,
                 17, "CkTypeId '{ckTypeId}' defines an outgoing AssociationRoleId '{ckAssociationId}' to CkTypeId '{targetCkTypeId}' by inheritance that violates the unique association role id constraint",
                 new [] {"ckTypeId", "ckAssociationId", "targetCkTypeId"})
        },
        {
            "CkTypeIdUnknownTargetCkTypeIdForAssociation",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 18, "CkTypeId '{originCkTypeId}' defines a unknown target CkTypeId '{targetCkTypeId}' for role id '{roleId}'. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"originCkTypeId", "targetCkTypeId", "roleId"})
        },
        {
            "CkTypeIdUnknown",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 19, "CkTypeId '{ckTypeId}' is unknown. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckTypeId"})
        },
        {
            "CkTypeIdMultipleOutgoingAssociationRepresentingSameRole",
             new CompilerMessageTemplate(MessageLevel.Error,
                 20, "CkTypeId '{ckTypeId}' defines an outgoing AssociationRoleId '{ckAssociationId}' to CkTypeId '{targetCkTypeId}'. This association is also defined between CkTypeId '{otherCkTypeId}' and target CkTypeId '{otherTargetCkTypeId}'.",
                 new [] {"ckTypeId", "ckAssociationId", "targetCkTypeId", "otherCkTypeId", "otherTargetCkTypeId"})
        },
        {
            "DerivedFromCkTypeIdThatIsFinal",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 21, "CkTypeId '{baseCkTypeId}' is final, but CkTypeId '{derivedTypeId}' is derived from it.",
                 new [] {"baseCkTypeId", "derivedTypeId"})
        },
        {
            "DirectoryMustBeEmpty",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 22, "Directory '{directory}' must be empty.",
                 new [] {"directory"})
        },
        {
            "ModelIdContainsInvalidCharacters",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 23, "ModelId '{modelId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"modelId"})
        },
        {
            "CkTypeIdContainsInvalidCharacters",
             new CompilerMessageTemplate(MessageLevel.Error,
                 24, "CkTypeId '{ckTypeId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckTypeId"})
        },
        {
            "CkAttributeIdContainsInvalidCharacters",
             new CompilerMessageTemplate(MessageLevel.Error,
                 25, "CkAttributeId '{ckAttributeId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckAttributeId"})
        },
        {
            "CkAssociationIdContainsInvalidCharacters",
             new CompilerMessageTemplate(MessageLevel.Error,
                 26, "CkAssociationId '{ckAssociationId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckAssociationId"})
        },
        {
            "SchemaValidationError",
             new CompilerMessageTemplate(MessageLevel.Error,
                 27, "Schema validation failed: '{errorMessage}'",
                 new [] {"errorMessage"})
        },
        {
            "DirectoryDoesNotExist",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 28, "Directory '{directoryPath}' does not exist.",
                 new [] {"directoryPath"})
        },
        {
            "FileDoesNotExist",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 29, "File '{filePath}' does not exist.",
                 new [] {"filePath"})
        },
        {
            "SelectionValueNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 30, "CkEnumId '{ckEnumId}' has defined key '{key}' which is used several times.",
                 new [] {"ckEnumId", "key"})
        },
        {
            "CkRecordIdUndefined",
             new CompilerMessageTemplate(MessageLevel.Error,
                 31, "CkAttributeId '{ckAttributeId}' is defined as Record, but the ValueCkRecordId is missing.",
                 new [] {"ckAttributeId"})
        },
        {
            "CkRecordIdContainsInvalidCharacters",
             new CompilerMessageTemplate(MessageLevel.Error,
                 32, "CkRecordId '{ckRecordId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckRecordId"})
        },
        {
            "RecordIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 33, "RecordId '{ckRecordId}' is not unique.",
                 new [] {"ckRecordId"})
        },
        {
            "CkRecordIdUnknown",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 34, "CkRecordId '{ckRecordId}' is unknown. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckRecordId"})
        },
        {
            "UnknownCkRecordIdForInheritance",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 35, "CkRecordId '{ckRecordId}' is unknown for inheritance. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckRecordId"})
        },
        {
            "DerivedFromCkRecordIdThatIsFinal",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 36, "CkRecordId '{baseCkRecordId}' is final, but CkRecordId '{derivedCkRecordId}' is derived from it.",
                 new [] {"baseCkRecordId", "derivedCkRecordId"})
        },
        {
            "CkRecordIdAttributeNameNotUnique",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 37, "CkRecordId '{ckRecordId}' defines attribute name '{attributeName}' that violates the unique attribute name constraint.",
                 new [] {"ckRecordId", "attributeName"})
        },
        {
            "CkRecordIdAttributeIdNotUniqueByInheritance",
             new CompilerMessageTemplate(MessageLevel.Error,
                 38, "CkRecordId '{ckRecordId}' defines AttributeId '{ckAttributeId}' by inheritance that violates the unique attribute id constraint.",
                 new [] {"ckRecordId", "ckAttributeId"})
        },
        {
            "CkRecordIdAttributeIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.FatalError,
                 39, "CkRecordId '{ckRecordId}' defines AttributeId '{ckAttributeId}' that violates the unique attribute id constraint.",
                 new [] {"ckRecordId", "ckAttributeId"})
        },
        {
            "CkRecordIdAttributeNameNotUniqueByInheritance",
             new CompilerMessageTemplate(MessageLevel.Error,
                 40, "CkRecordId '{ckRecordId}' defines attribute name '{attributeName}' by inheritance that violates the unique attribute name constraint.",
                 new [] {"ckRecordId", "attributeName"})
        },
        {
            "AttributeUsesUnknownCkRecordId",
             new CompilerMessageTemplate(MessageLevel.Error,
                 41, "CkAttributeId '{ckAttributeId}' uses unknown CkRecordId '{ckRecordId}'. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckAttributeId", "ckRecordId"})
        },
        {
            "UnknownAttributeOfCkRecordIdInSource",
             new CompilerMessageTemplate(MessageLevel.Error,
                 42, "Attribute Id '{ckAttributeId}' of CkRecordId '{ckRecordId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckAttributeId", "ckRecordId"})
        },
        {
            "UnknownDerivedFromCkRecordIdInSource",
             new CompilerMessageTemplate(MessageLevel.Error,
                 43, "Derived CkRecordId '{derivedCkRecordId}' of CkRecordId '{ckRecordId}' does not exist. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"derivedCkRecordId", "ckRecordId"})
        },
        {
            "CkEnumIdContainsInvalidCharacters",
             new CompilerMessageTemplate(MessageLevel.Error,
                 44, "CkEnumId '{ckEnumId}' contains invalid characters. Allowed characters are A-Z, a-z, 0-9, . and _.",
                 new [] {"ckEnumId"})
        },
        {
            "EnumIdNotUnique",
             new CompilerMessageTemplate(MessageLevel.Error,
                 45, "CkEnumId '{ckEnumId}' is not unique.",
                 new [] {"ckEnumId"})
        },
        {
            "CkEnumIdUndefined",
             new CompilerMessageTemplate(MessageLevel.Error,
                 46, "CkAttributeId '{ckAttributeId}' is defined as Enum, but the ValueCkEnumId is missing.",
                 new [] {"ckAttributeId"})
        },
        {
            "CkTypeIdUnknownTargetAttributeIdForAssociation",
             new CompilerMessageTemplate(MessageLevel.Error,
                 47, "CkTypeId '{originCkTypeId}' defines for role id '{roleId}' an unknown target AttributeId '{targetCkAttributeId}' for CkType '{targetCkTypeId}'.",
                 new [] {"originCkTypeId", "roleId", "targetCkAttributeId", "targetCkTypeId"})
        },
    };
}

