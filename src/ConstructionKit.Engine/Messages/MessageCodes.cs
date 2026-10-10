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

    internal static OperationMessage DisplayRuleSyntaxInvalid(string? location, object ruleProperty, object ckTypeId, object errorMessage) =>
        GetMessage("DisplayRuleSyntaxInvalid", location, ruleProperty, ckTypeId, errorMessage);

    internal static OperationMessage DisplayRuleAttributePathUnknown(string? location, object ruleProperty, object ckTypeId, object attributePath) =>
        GetMessage("DisplayRuleAttributePathUnknown", location, ruleProperty, ckTypeId, attributePath);

    internal static OperationMessage OwnerAttributeInvalid(string? location, object ckTypeId, object ownerAttributePath, object reason) =>
        GetMessage("OwnerAttributeInvalid", location, ckTypeId, ownerAttributePath, reason);

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

    internal static OperationMessage AttributeUsesUnknownCkEnumId(string? location, object ckAttributeId, object ckEnumId) =>
        GetMessage("AttributeUsesUnknownCkEnumId", location, ckAttributeId, ckEnumId);

    internal static OperationMessage AttributeIsEnumButValueIsNotSet(string? location, object ckAttributeId) =>
        GetMessage("AttributeIsEnumButValueIsNotSet", location, ckAttributeId);

    internal static OperationMessage AttributeIsRecordButValueIsNotSet(string? location, object ckAttributeId) =>
        GetMessage("AttributeIsRecordButValueIsNotSet", location, ckAttributeId);

    internal static OperationMessage VariableUnknown(string? location, object variableName) =>
        GetMessage("VariableUnknown", location, variableName);

    internal static OperationMessage MigrationMetaParseError(string? location, object errorMessage) =>
        GetMessage("MigrationMetaParseError", location, errorMessage);

    internal static OperationMessage MigrationScriptNotFound(string? location) =>
        GetMessage("MigrationScriptNotFound", location);

    internal static OperationMessage MigrationScriptParseError(string? location, object errorMessage) =>
        GetMessage("MigrationScriptParseError", location, errorMessage);

    internal static OperationMessage MultipleVersionsOfCkModelResolved(string? location, object modelName, object versions, object origins) =>
        GetMessage("MultipleVersionsOfCkModelResolved", location, modelName, versions, origins);

    internal static OperationMessage SecretAttributeHasDefaultValues(string? location, object ckAttributeId) =>
        GetMessage("SecretAttributeHasDefaultValues", location, ckAttributeId);

    internal static OperationMessage SecretAttributeAssignmentInvalid(string? location, object ckElementId, object attributeName, object ckAttributeId, object reason) =>
        GetMessage("SecretAttributeAssignmentInvalid", location, ckElementId, attributeName, ckAttributeId, reason);

    internal static OperationMessage SecretAttributeOwnershipNotSecret(string? location, object ckElementId, object attributeName, object ownership) =>
        GetMessage("SecretAttributeOwnershipNotSecret", location, ckElementId, attributeName, ownership);

    internal static OperationMessage SecretAttributeIndexed(string? location, object ckTypeId, object attributePath) =>
        GetMessage("SecretAttributeIndexed", location, ckTypeId, attributePath);

    internal static OperationMessage SecretAttributeReferencedByDisplayRule(string? location, object ruleProperty, object ckTypeId, object attributePath) =>
        GetMessage("SecretAttributeReferencedByDisplayRule", location, ruleProperty, ckTypeId, attributePath);

    internal static OperationMessage SecretAttributeRequiresSystemDependency(string? location, object modelId, object minimumVersion, object declaredDependency) =>
        GetMessage("SecretAttributeRequiresSystemDependency", location, modelId, minimumVersion, declaredDependency);

    internal static OperationMessage RecordWithSecretRequiresRecordKey(string? location, object ckRecordId, object secretAttributes) =>
        GetMessage("RecordWithSecretRequiresRecordKey", location, ckRecordId, secretAttributes);

    internal static OperationMessage RecordKeyInvalid(string? location, object ckRecordId, object recordKey, object reason) =>
        GetMessage("RecordKeyInvalid", location, ckRecordId, recordKey, reason);

    internal static OperationMessage CkLanguageFeatureRequiresV2(string? location, object modelId, object feature, object element, object ckLanguage) =>
        GetMessage("CkLanguageFeatureRequiresV2", location, modelId, feature, element, ckLanguage);

    internal static OperationMessage CkLanguageNotSupported(string? location, object modelId, object ckLanguage, object maxCkLanguage) =>
        GetMessage("CkLanguageNotSupported", location, modelId, ckLanguage, maxCkLanguage);

    internal static OperationMessage CkInterfaceIdNotUnique(string? location, object ckInterfaceId) =>
        GetMessage("CkInterfaceIdNotUnique", location, ckInterfaceId);

    internal static OperationMessage CkInterfaceNameCollidesWithType(string? location, object ckInterfaceId, object ckTypeId) =>
        GetMessage("CkInterfaceNameCollidesWithType", location, ckInterfaceId, ckTypeId);

    internal static OperationMessage CkInterfaceAttributeUnknown(string? location, object attributeName, object ckInterfaceId, object ckAttributeId) =>
        GetMessage("CkInterfaceAttributeUnknown", location, attributeName, ckInterfaceId, ckAttributeId);

    internal static OperationMessage ImplementsUnknownCkInterface(string? location, object ckTypeId, object ckInterfaceId) =>
        GetMessage("ImplementsUnknownCkInterface", location, ckTypeId, ckInterfaceId);

    internal static OperationMessage CkInterfaceMemberMissing(string? location, object ckTypeId, object ckInterfaceId, object ckAttributeId, object attributeName) =>
        GetMessage("CkInterfaceMemberMissing", location, ckTypeId, ckInterfaceId, ckAttributeId, attributeName);

    internal static OperationMessage CkInterfaceMemberMultiplicityMismatch(string? location, object ckTypeId, object ckInterfaceId, object ckAttributeId, object attributeName) =>
        GetMessage("CkInterfaceMemberMultiplicityMismatch", location, ckTypeId, ckInterfaceId, ckAttributeId, attributeName);

    internal static OperationMessage CkInterfaceMemberNameMismatch(string? location, object ckTypeId, object ckInterfaceId, object ckAttributeId, object assignedName, object attributeName) =>
        GetMessage("CkInterfaceMemberNameMismatch", location, ckTypeId, ckInterfaceId, ckAttributeId, assignedName, attributeName);

    internal static OperationMessage CkInterfaceMemberHidden(string? location, object ckTypeId, object ckInterfaceId, object ckAttributeId, object attributeName) =>
        GetMessage("CkInterfaceMemberHidden", location, ckTypeId, ckInterfaceId, ckAttributeId, attributeName);

    internal static OperationMessage CkMethodIdNotUnique(string? location, object methodId, object ckTypeId, object reason) =>
        GetMessage("CkMethodIdNotUnique", location, methodId, ckTypeId, reason);

    internal static OperationMessage CkMethodParameterInvalid(string? location, object methodId, object ckTypeId, object reason) =>
        GetMessage("CkMethodParameterInvalid", location, methodId, ckTypeId, reason);

    internal static OperationMessage CkMethodNameReserved(string? location, object methodId, object ckTypeId, object methodName) =>
        GetMessage("CkMethodNameReserved", location, methodId, ckTypeId, methodName);

    internal static OperationMessage CkMethodErrorCodeInvalid(string? location, object code, object methodId, object ckTypeId, object reason) =>
        GetMessage("CkMethodErrorCodeInvalid", location, code, methodId, ckTypeId, reason);

    internal static OperationMessage CkMethodAuthorizationInvalid(string? location, object methodId, object ckTypeId, object reason) =>
        GetMessage("CkMethodAuthorizationInvalid", location, methodId, ckTypeId, reason);

    internal static OperationMessage CkInterfaceMemberNotUnique(string? location, object ckInterfaceId, object what, object value) =>
        GetMessage("CkInterfaceMemberNotUnique", location, ckInterfaceId, what, value);

    internal static OperationMessage RestrictedAttributeInDerivedRule(string? location, object ruleProperty, object path, object ckTypeId, object attributeName, object access) =>
        GetMessage("RestrictedAttributeInDerivedRule", location, ruleProperty, path, ckTypeId, attributeName, access);

    internal static OperationMessage CkModelRequiresNewerEngine(string? location, object modelId, object minEngineVersion, object engineVersion) =>
        GetMessage("CkModelRequiresNewerEngine", location, modelId, minEngineVersion, engineVersion);

    internal static OperationMessage HiddenAttributeIndexed(string? location, object indexType, object ckTypeId, object attributeName, object attributePath) =>
        GetMessage("HiddenAttributeIndexed", location, indexType, ckTypeId, attributeName, attributePath);

    internal static OperationMessage HiddenAttributeAutoCompleteValues(string? location, object attributeName, object ckElementId) =>
        GetMessage("HiddenAttributeAutoCompleteValues", location, attributeName, ckElementId);

    internal static OperationMessage HiddenAttributeOnAssociationRole(string? location, object ckRoleId, object attributeName) =>
        GetMessage("HiddenAttributeOnAssociationRole", location, ckRoleId, attributeName);

    internal static OperationMessage UnknownIndexAttributePath(string? location, object indexType, object ckTypeId, object attributePath, object segment) =>
        GetMessage("UnknownIndexAttributePath", location, indexType, ckTypeId, attributePath, segment);

    internal static OperationMessage CkReferenceToInternalElement(string? location, object element, object kind, object referencedElement) =>
        GetMessage("CkReferenceToInternalElement", location, element, kind, referencedElement);

    internal static OperationMessage CkElementNotDerivable(string? location, object kind, object element, object baseElement) =>
        GetMessage("CkElementNotDerivable", location, kind, element, baseElement);

    internal static OperationMessage CkInterfaceExtendsInvalid(string? location, object ckInterfaceId, object extendedInterfaceId, object reason) =>
        GetMessage("CkInterfaceExtendsInvalid", location, ckInterfaceId, extendedInterfaceId, reason);

    internal static OperationMessage CkInterfaceMemberConflict(string? location, object ckInterfaceId, object reason) =>
        GetMessage("CkInterfaceMemberConflict", location, ckInterfaceId, reason);

    internal static OperationMessage CkInterfaceAssociationInvalid(string? location, object ckRoleId, object ckInterfaceId, object reason) =>
        GetMessage("CkInterfaceAssociationInvalid", location, ckRoleId, ckInterfaceId, reason);

    internal static OperationMessage CkInterfaceAssociationMissing(string? location, object ckTypeId, object ckInterfaceId, object ckRoleId, object target, object multiplicity) =>
        GetMessage("CkInterfaceAssociationMissing", location, ckTypeId, ckInterfaceId, ckRoleId, target, multiplicity);

    internal static OperationMessage CkInterfaceMethodConflict(string? location, object ckTypeId, object methodId, object ckInterfaceId) =>
        GetMessage("CkInterfaceMethodConflict", location, ckTypeId, methodId, ckInterfaceId);

    internal static OperationMessage CkInterfaceHasNoMembers(string? location, object ckInterfaceId) =>
        GetMessage("CkInterfaceHasNoMembers", location, ckInterfaceId);

    internal static OperationMessage CkInterfaceDeprecated(string? location, object element, object ckInterfaceId) =>
        GetMessage("CkInterfaceDeprecated", location, element, ckInterfaceId);

    internal static OperationMessage UnknownTargetCkInterfaceOfAssociation(string? location, object ckRoleId, object ckTypeId, object ckInterfaceId) =>
        GetMessage("UnknownTargetCkInterfaceOfAssociation", location, ckRoleId, ckTypeId, ckInterfaceId);

    internal static OperationMessage CkMethodGeneratedNameCollision(string? location, object first, object second, object modelId, object generatedName) =>
        GetMessage("CkMethodGeneratedNameCollision", location, first, second, modelId, generatedName);

    internal static OperationMessage CkElementsInWrongFolder(string? location, object file, object key, object expectedFolder) =>
        GetMessage("CkElementsInWrongFolder", location, file, key, expectedFolder);

    internal static OperationMessage CkInconsistentVisibility(string? location, object element, object reason) =>
        GetMessage("CkInconsistentVisibility", location, element, reason);

    private static readonly Dictionary<string, OperationMessageTemplate> Templates = new()
    {
        {
            "UnknownCkModel",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 1, "'{modelId}' is not a known construction kit model. Please check if you have set dependency to the correct construction kit model.",
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
                 11, "CkTypeId '{ckTypeId}' is used as base type but is an unknown CkTypeId. This may happen because a dependency to another construction kit model is missing.",
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
                 35, "CkRecordId '{ckRecordId}' is used as base record type but is an unknown CkRecordId. This may happen because a dependency to another construction kit model is missing.",
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
        {
            "AttributeUsesUnknownCkEnumId",
             new OperationMessageTemplate(MessageLevel.Error,
                 59, "CkAttributeId '{ckAttributeId}' uses unknown CkEnumId '{ckEnumId}'. This may happen because a dependency to another construction kit model is missing.",
                 new [] {"ckAttributeId", "ckEnumId"})
        },
        {
            "AttributeIsEnumButValueIsNotSet",
             new OperationMessageTemplate(MessageLevel.Error,
                 60, "CkAttributeId '{ckAttributeId}' defines an enum but the enum reference (ValueCkEnumId) is not set.",
                 new [] {"ckAttributeId"})
        },
        {
            "AttributeIsRecordButValueIsNotSet",
             new OperationMessageTemplate(MessageLevel.Error,
                 61, "CkAttributeId '{ckAttributeId}' defines an record or record array but the record reference (ValueCkRecordId) is not set.",
                 new [] {"ckAttributeId"})
        },
        {
            "VariableUnknown",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 62, "Variable '{variableName}' is undefined.",
                 new [] {"variableName"})
        },
        {
            "MigrationMetaParseError",
             new OperationMessageTemplate(MessageLevel.Warning,
                 63, "Failed to parse migration meta file: {errorMessage}",
                 new [] {"errorMessage"})
        },
        {
            "MigrationScriptNotFound",
             new OperationMessageTemplate(MessageLevel.Error,
                 64, "Migration script file not found.",
                 new string[] {})
        },
        {
            "MigrationScriptParseError",
             new OperationMessageTemplate(MessageLevel.Error,
                 65, "Failed to parse migration script file: {errorMessage}",
                 new [] {"errorMessage"})
        },
        {
            "MultipleVersionsOfCkModelResolved",
             new OperationMessageTemplate(MessageLevel.FatalError,
                 66, "Multiple versions of construction kit model '{modelName}' were resolved as transitive dependencies: {versions}. Conflicting versions are referenced by: {origins}. This typically happens when different catalogs (LocalFileSystem, public/private GitHub) hold dependents that pin different versions of the same model. Resolutions: rebuild the conflicting dependents against a single common version, narrow the dependency range in the consumer's ckModel.yaml, or disable catalogs that hold stale entries (MSBuild properties OctoPublicGitHubCatalogIsEnabled / OctoPrivateGitHubCatalogIsEnabled).",
                 new [] {"modelName", "versions", "origins"})
        },
        {
            "DisplayRuleSyntaxInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 67, "Display rule '{ruleProperty}' of type '{ckTypeId}' is invalid: {errorMessage}",
                 new [] {"ruleProperty", "ckTypeId", "errorMessage"})
        },
        {
            "DisplayRuleAttributePathUnknown",
             new OperationMessageTemplate(MessageLevel.Error,
                 68, "Display rule '{ruleProperty}' of type '{ckTypeId}' references unknown attribute path '{attributePath}'. Only own attributes (including record paths) can be referenced; associations are not supported.",
                 new [] {"ruleProperty", "ckTypeId", "attributePath"})
        },
        {
            "OwnerAttributeInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 69, "Owner attribute '{ownerAttributePath}' of type '{ckTypeId}' is invalid: {reason}",
                 new [] {"ckTypeId", "ownerAttributePath", "reason"})
        },
        {
            "SecretAttributeHasDefaultValues",
             new OperationMessageTemplate(MessageLevel.Error,
                 70, "Secret attribute '{ckAttributeId}' declares defaultValues. A Secret attribute cannot have default values - a credential must never ship with the model; set it after installation.",
                 new [] {"ckAttributeId"})
        },
        {
            "SecretAttributeAssignmentInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 71, "Secret attribute '{attributeName}' ('{ckAttributeId}') of '{ckElementId}' is invalid: {reason}",
                 new [] {"ckElementId", "attributeName", "ckAttributeId", "reason"})
        },
        {
            "SecretAttributeOwnershipNotSecret",
             new OperationMessageTemplate(MessageLevel.Error,
                 72, "Secret attribute '{attributeName}' of '{ckElementId}' declares ownership '{ownership}'. The effective ownership of a Secret attribute is always 'Secret'; remove the override or declare 'ownership: Secret'.",
                 new [] {"ckElementId", "attributeName", "ownership"})
        },
        {
            "SecretAttributeIndexed",
             new OperationMessageTemplate(MessageLevel.Error,
                 73, "Index of type '{ckTypeId}' references Secret attribute path '{attributePath}'. Secret attributes cannot be indexed - the stored value is ciphertext.",
                 new [] {"ckTypeId", "attributePath"})
        },
        {
            "SecretAttributeReferencedByDisplayRule",
             new OperationMessageTemplate(MessageLevel.Error,
                 74, "Display rule '{ruleProperty}' of type '{ckTypeId}' references Secret attribute path '{attributePath}'. Display rules cannot reveal Secret attributes.",
                 new [] {"ruleProperty", "ckTypeId", "attributePath"})
        },
        {
            "SecretAttributeRequiresSystemDependency",
             new OperationMessageTemplate(MessageLevel.Error,
                 75, "Construction kit model '{modelId}' uses Secret attributes and must depend on System >= {minimumVersion} (declared: {declaredDependency}). Declare the dependency as 'System-[{minimumVersion},3.0)' so engines that do not know the Secret value type fail with a dependency error.",
                 new [] {"modelId", "minimumVersion", "declaredDependency"})
        },
        {
            "RecordWithSecretRequiresRecordKey",
             new OperationMessageTemplate(MessageLevel.Error,
                 76, "Record '{ckRecordId}' contains Secret attribute(s) {secretAttributes} but declares no 'recordKey'. A record with Secret sub-attributes must name the sub-attribute that identifies an element, so a secret left empty on a record array replace can be carried over from the stored element with the same key.",
                 new [] {"ckRecordId", "secretAttributes"})
        },
        {
            "RecordKeyInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 77, "Record key '{recordKey}' of record '{ckRecordId}' is invalid: {reason}",
                 new [] {"ckRecordId", "recordKey", "reason"})
        },
        {
            "CkLanguageFeatureRequiresV2",
             new OperationMessageTemplate(MessageLevel.Error,
                 90, "Model '{modelId}' uses the CK v2 feature '{feature}' at '{element}', which requires 'ckLanguage: 2' in ckModel.yaml (declared: {ckLanguage}).",
                 new [] {"modelId", "feature", "element", "ckLanguage"})
        },
        {
            "CkLanguageNotSupported",
             new OperationMessageTemplate(MessageLevel.Error,
                 91, "Model '{modelId}' declares ckLanguage {ckLanguage}, but this engine supports ckLanguage 1 to {maxCkLanguage}. Use an engine that supports the model's CK language version.",
                 new [] {"modelId", "ckLanguage", "maxCkLanguage"})
        },
        {
            "CkInterfaceIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 92, "Interface '{ckInterfaceId}' is defined more than once.",
                 new [] {"ckInterfaceId"})
        },
        {
            "CkInterfaceNameCollidesWithType",
             new OperationMessageTemplate(MessageLevel.Error,
                 93, "Interface '{ckInterfaceId}' has the same name as type '{ckTypeId}' of the same model. Interface and type names of a model must differ (they share the GraphQL type namespace).",
                 new [] {"ckInterfaceId", "ckTypeId"})
        },
        {
            "CkInterfaceAttributeUnknown",
             new OperationMessageTemplate(MessageLevel.Error,
                 94, "Member '{attributeName}' of interface '{ckInterfaceId}' references unknown attribute '{ckAttributeId}'. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"attributeName", "ckInterfaceId", "ckAttributeId"})
        },
        {
            "ImplementsUnknownCkInterface",
             new OperationMessageTemplate(MessageLevel.Error,
                 95, "Type '{ckTypeId}' implements unknown interface '{ckInterfaceId}'. Please check if you have set dependency to the correct construction kit model.",
                 new [] {"ckTypeId", "ckInterfaceId"})
        },
        {
            "CkInterfaceMemberMissing",
             new OperationMessageTemplate(MessageLevel.Error,
                 96, "Type '{ckTypeId}' implements '{ckInterfaceId}' but does not assign required member '{ckAttributeId}' (name '{attributeName}').",
                 new [] {"ckTypeId", "ckInterfaceId", "ckAttributeId", "attributeName"})
        },
        {
            "CkInterfaceMemberMultiplicityMismatch",
             new OperationMessageTemplate(MessageLevel.Error,
                 97, "Type '{ckTypeId}' implements '{ckInterfaceId}' but assigns required member '{ckAttributeId}' (name '{attributeName}') with 'isOptional: true'. A required interface member must be required on the implementing type.",
                 new [] {"ckTypeId", "ckInterfaceId", "ckAttributeId", "attributeName"})
        },
        {
            "CkInterfaceMemberNameMismatch",
             new OperationMessageTemplate(MessageLevel.Error,
                 98, "Type '{ckTypeId}' implements '{ckInterfaceId}' and assigns member '{ckAttributeId}' under the name '{assignedName}', expected '{attributeName}'.",
                 new [] {"ckTypeId", "ckInterfaceId", "ckAttributeId", "assignedName", "attributeName"})
        },
        {
            "CkInterfaceMemberHidden",
             new OperationMessageTemplate(MessageLevel.Error,
                 99, "Type '{ckTypeId}' implements '{ckInterfaceId}' but assigns member '{ckAttributeId}' (name '{attributeName}') with 'access: Hidden'. Interface members must be visible.",
                 new [] {"ckTypeId", "ckInterfaceId", "ckAttributeId", "attributeName"})
        },
        {
            "CkMethodIdNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 100, "Method '{methodId}' of type '{ckTypeId}' is not unique: {reason}",
                 new [] {"methodId", "ckTypeId", "reason"})
        },
        {
            "CkMethodParameterInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 101, "Method '{methodId}' of type '{ckTypeId}' is invalid: {reason}",
                 new [] {"methodId", "ckTypeId", "reason"})
        },
        {
            "CkMethodNameReserved",
             new OperationMessageTemplate(MessageLevel.Error,
                 102, "Method '{methodId}' of type '{ckTypeId}' uses the reserved name '{methodName}'. The names 'Create', 'Update' and 'Delete' are reserved for the generic mutations (case-insensitive).",
                 new [] {"methodId", "ckTypeId", "methodName"})
        },
        {
            "CkMethodErrorCodeInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 103, "Error code '{code}' of method '{methodId}' of type '{ckTypeId}' is invalid: {reason}",
                 new [] {"code", "methodId", "ckTypeId", "reason"})
        },
        {
            "CkMethodAuthorizationInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 104, "Authorization of method '{methodId}' of type '{ckTypeId}' is invalid: {reason}",
                 new [] {"methodId", "ckTypeId", "reason"})
        },
        {
            "RestrictedAttributeInDerivedRule",
             new OperationMessageTemplate(MessageLevel.Error,
                 105, "{ruleProperty} '{path}' of type '{ckTypeId}' references attribute '{attributeName}' with 'access: {access}'. Display rules, owner attribute paths and text index paths are readable through other fields (rtDisplayName, filters, search) and must not expose it.",
                 new [] {"ruleProperty", "path", "ckTypeId", "attributeName", "access"})
        },
        {
            "CkInterfaceMemberNotUnique",
             new OperationMessageTemplate(MessageLevel.Error,
                 127, "Interface '{ckInterfaceId}' declares the member {what} '{value}' more than once. Each attribute and each member name may appear only once in an interface.",
                 new [] {"ckInterfaceId", "what", "value"})
        },
        {
            "CkModelRequiresNewerEngine",
             new OperationMessageTemplate(MessageLevel.Error,
                 126, "Model '{modelId}' requires construction kit engine version {minEngineVersion} or later (minEngineVersion); this engine is version {engineVersion}. Update the service before importing the model.",
                 new [] {"modelId", "minEngineVersion", "engineVersion"})
        },
        {
            "HiddenAttributeIndexed",
             new OperationMessageTemplate(MessageLevel.Error,
                 106, "Index ({indexType}) of type '{ckTypeId}' references the Hidden attribute '{attributeName}' through path '{attributePath}'. Hidden attributes cannot be indexed: a unique index reveals values through duplicate-key errors.",
                 new [] {"indexType", "ckTypeId", "attributeName", "attributePath"})
        },
        {
            "HiddenAttributeAutoCompleteValues",
             new OperationMessageTemplate(MessageLevel.Error,
                 107, "Attribute '{attributeName}' of '{ckElementId}' is Hidden and declares 'autoCompleteValues', which publish candidate values in the construction kit model. Remove the values or the Hidden access.",
                 new [] {"attributeName", "ckElementId"})
        },
        {
            "HiddenAttributeOnAssociationRole",
             new OperationMessageTemplate(MessageLevel.Error,
                 108, "Association role '{ckRoleId}' assigns attribute '{attributeName}' with access Hidden. Association attributes have no access guard; Hidden is not supported on association roles.",
                 new [] {"ckRoleId", "attributeName"})
        },
        {
            "UnknownIndexAttributePath",
             new OperationMessageTemplate(MessageLevel.Error,
                 109, "Index ({indexType}) of type '{ckTypeId}' references the unknown attribute path '{attributePath}' (segment '{segment}'). Index paths are resolved case-insensitively by the database; in a ckLanguage 2 model every segment must name an attribute of the type or of the record it traverses.",
                 new [] {"indexType", "ckTypeId", "attributePath", "segment"})
        },
        {
            "CkReferenceToInternalElement",
             new OperationMessageTemplate(MessageLevel.Error,
                 112, "'{element}' references the internal {kind} '{referencedElement}' of another model. Internal elements (visibility: Internal) can only be referenced inside their own model.",
                 new [] {"element", "kind", "referencedElement"})
        },
        {
            "CkElementNotDerivable",
             new OperationMessageTemplate(MessageLevel.Error,
                 113, "{kind} '{element}' derives from '{baseElement}' of another model, which only its own model may derive from (derivable: Model). The base model must declare 'derivable: Any' to allow it.",
                 new [] {"kind", "element", "baseElement"})
        },
        {
            "CkInterfaceExtendsInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 118, "Interface '{ckInterfaceId}' extends '{extendedInterfaceId}': {reason}.",
                 new [] {"ckInterfaceId", "extendedInterfaceId", "reason"})
        },
        {
            "CkInterfaceMemberConflict",
             new OperationMessageTemplate(MessageLevel.Error,
                 119, "Interface '{ckInterfaceId}' has conflicting members: {reason}. Members inherited through 'extends' and own members must agree on name and attribute.",
                 new [] {"ckInterfaceId", "reason"})
        },
        {
            "CkInterfaceAssociationInvalid",
             new OperationMessageTemplate(MessageLevel.Error,
                 120, "Association member '{ckRoleId}' of interface '{ckInterfaceId}' is invalid: {reason}.",
                 new [] {"ckRoleId", "ckInterfaceId", "reason"})
        },
        {
            "CkInterfaceAssociationMissing",
             new OperationMessageTemplate(MessageLevel.Error,
                 121, "Type '{ckTypeId}' implements interface '{ckInterfaceId}' but has no outbound association '{ckRoleId}' to {target}{multiplicity}. Required association members must be provided by the type or its base types.",
                 new [] {"ckTypeId", "ckInterfaceId", "ckRoleId", "target", "multiplicity"})
        },
        {
            "CkInterfaceMethodConflict",
             new OperationMessageTemplate(MessageLevel.Error,
                 122, "Type '{ckTypeId}' declares method '{methodId}', which interface '{ckInterfaceId}' declares with a different signature. A type may not redeclare an interface method with another signature.",
                 new [] {"ckTypeId", "methodId", "ckInterfaceId"})
        },
        {
            "CkInterfaceHasNoMembers",
             new OperationMessageTemplate(MessageLevel.Error,
                 123, "Interface '{ckInterfaceId}' declares no attribute, association or method member and extends no interface.",
                 new [] {"ckInterfaceId"})
        },
        {
            "CkInterfaceDeprecated",
             new OperationMessageTemplate(MessageLevel.Warning,
                 124, "'{element}' references the deprecated interface '{ckInterfaceId}'. It is removed in the next major version of its model; migrate to its successor.",
                 new [] {"element", "ckInterfaceId"})
        },
        {
            "UnknownTargetCkInterfaceOfAssociation",
             new OperationMessageTemplate(MessageLevel.Error,
                 128, "Association '{ckRoleId}' of type '{ckTypeId}' narrows its target to the unknown interface '{ckInterfaceId}' (targetCkInterfaceId).",
                 new [] {"ckRoleId", "ckTypeId", "ckInterfaceId"})
        },
        {
            "CkMethodGeneratedNameCollision",
             new OperationMessageTemplate(MessageLevel.Error,
                 125, "Methods '{first}' and '{second}' of model '{modelId}' produce the same generated name '{generatedName}' (constant '{generatedName}MethodId', record '{generatedName}Parameters'). Rename one of the types or methods.",
                 new [] {"first", "second", "modelId", "generatedName"})
        },
        {
            "CkElementsInWrongFolder",
             new OperationMessageTemplate(MessageLevel.Warning,
                 110, "File '{file}' declares '{key}', which is only read from files in the '{expectedFolder}/' folder. These elements are ignored; move them into '{expectedFolder}/'.",
                 new [] {"file", "key", "expectedFolder"})
        },
        {
            "CkInconsistentVisibility",
             new OperationMessageTemplate(MessageLevel.Error,
                 129, "Public '{element}' {reason}. A public element may only reference public elements of its own model (visibility consistency): make the referenced element public or '{element}' internal.",
                 new [] {"element", "reason"})
        },
    };
}

