namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception during model validation operations.
/// </summary>
public class ModelValidationException : CkModelException
{
    /// <inheritdoc />
    public ModelValidationException()
    {
    }

    /// <inheritdoc />
    public ModelValidationException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public ModelValidationException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception UnknownCkTypeIdForInheritance(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{ckTypeId}' is used as base type but is an unknown CkTypeId. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception UnknownCkTypeId(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{ckTypeId}' is unknown. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception UnknownCkRecordId(CkId<CkRecordId> ckRecordId)
    {
        return new ModelValidationException(
            $"CkRecordId '{ckRecordId}' is unknown. This may happen because a dependency to another construction kit model is missing.");
    }


    internal static Exception UnknownCkTypeIdForAssociationTarget(CkId<CkTypeId> originCkTypeId,
        CkId<CkAssociationRoleId> entityAssociationRoleId, CkId<CkTypeId> typeAssociationTargetCkTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{originCkTypeId}' defines a unknown target construction kit type id '{typeAssociationTargetCkTypeId}' for role id '{entityAssociationRoleId}'." +
            $" This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception DerivedFromCkTypeIdThatIsFinal(CkId<CkTypeId> currentCkTypeId, CkId<CkTypeId> lastCkTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{currentCkTypeId}' is final, but CkTypeId '{lastCkTypeId}' is derived from it.");
    }

    internal static Exception InheritanceMissing(string typeId)
    {
        return new ModelValidationException($"Name '{typeId}' has no inheritance definition. Ensure that attribute ckDerivedId is set.");
    }

    internal static Exception ModelIdContainsInvalidCharacters(string modelId)
    {
        return new ModelValidationException($"Name '{modelId}' contains invalid characters. Only a-z, A-Z, 0-9, _ and . are allowed.");
    }

    internal static Exception DerivedFromCkRecordIdThatIsFinal(CkId<CkRecordId> currentCkRecordId, CkId<CkRecordId> lastCkRecordId)
    {
        return new ModelValidationException(
            $"CkRecordId '{currentCkRecordId}' is final, but CkRecordId '{lastCkRecordId}' is derived from it.");
    }

    internal static Exception UnknownCkRecordIdForInheritance(CkId<CkRecordId> ckRecordId)
    {
        return new ModelValidationException(
            $"CkRecordId '{ckRecordId}' is used as base record type but is an unknown CkRecordId. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception DuplicateAttributeNamesInCkRecord(CkId<CkRecordId> ckRecordId, IEnumerable<string> select)
    {
        var attributeNames = string.Join(", ", select);
        return new ModelValidationException($"CkRecordId '{ckRecordId}' has duplicate attribute names: '{attributeNames}'");
    }

    internal static Exception DuplicateAttributeIdsInCkRecord(CkId<CkRecordId> ckRecordId,
        IEnumerable<CkId<CkAttributeId>> duplicateAttributeIds)
    {
        var attributeIds = string.Join(", ", duplicateAttributeIds);
        return new ModelValidationException($"CkRecordId '{ckRecordId}' has duplicate attribute IDs: '{attributeIds}'");
    }
}