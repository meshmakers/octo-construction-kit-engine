using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Throws when an error occurs in a runtime repository
/// </summary>
public class RuntimeRepositoryException : PersistenceException
{
    /// <inheritdoc />
    private RuntimeRepositoryException(string message) : base(message)
    {
        OperationResult = new OperationResult();
    }

    /// <inheritdoc />
    private RuntimeRepositoryException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }

    /// <inheritdoc />
    private RuntimeRepositoryException(string message, Exception inner, OperationResult operationResult) : base(message, inner)
    {
        OperationResult = operationResult;
    }

    /// <summary>
    ///     The <see cref="OperationResult" /> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }


    internal static Exception CkTypeIdMissingForType(Type type)
    {
        return new RuntimeRepositoryException($"No Construction Kit Id for type '{type.FullName}'" +
                                              $" is defined. Is attribute '{typeof(RtCkIdAttribute).FullName}' missing?");
    }

    internal static Exception RtCkTypeIdDoesNotExistInCache(RtCkId<CkTypeId> rtCkTypeId)
    {
        return new RuntimeRepositoryException($"RtCkTypeId '{rtCkTypeId}' was not found in model cache." +
                                              " Wrong RtCkTypeId used?");
    }

    internal static Exception DocumentAlreadyAdded(string tenantId, object key)
    {
        return new RuntimeRepositoryException($"Document '{key}' already added to tenant '{tenantId}'.");
    }

    internal static Exception CkTypeIdIsAbstract(string tenantId, CkId<CkTypeId> ckTypeId)
    {
        return new RuntimeRepositoryException($"CkTypeId '{ckTypeId}' is abstract in tenant '{tenantId}'.");
    }

    internal static void ThrowIfOperationResultError(OperationResult operationResult)
    {
        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            var messages = operationResult.GetMessages();
            throw new RuntimeRepositoryException($"Operation result contains errors.{Environment.NewLine}{messages}", operationResult);
        }
    }

    internal static Exception DocumentDoesNotExist(string tenantId, object key, Type documentType)
    {
        throw new RuntimeRepositoryException(
            $"Document with key '{key}' of type '{documentType.FullName}' does not exist in tenant '{tenantId}'.");
    }

    internal static Exception FieldFilterDidNotReturnResult(Type type, FieldFilterCriteria fieldFilterCriteria)
    {
        return new RuntimeRepositoryException(
            $"Field filter did not return a result for type '{type.FullName}' with criteria: {fieldFilterCriteria}");
    }

    internal static Exception AttributeFilterNotSupportedByDataSource(Type type)
    {
        return new RuntimeRepositoryException($"Attribute filter is not supported by data source for type '{type.FullName}'.");
    }

    internal static Exception TextFilterNotSupportedByDataSource(Type type)
    {
        return new RuntimeRepositoryException($"Text filter is not supported by data source for type '{type.FullName}'.");
    }

    internal static Exception SortOrderNotSupportedByDataSource(Type type)
    {
        return new RuntimeRepositoryException($"Sort order is not supported by data source for type '{type.FullName}'.");
    }

    internal static Exception AttributeWithNameDoesNotExist(CkId<CkTypeId> ckTypeId, string filterAttributeName)
    {
        return new RuntimeRepositoryException($"Attribute with name '{filterAttributeName}' does not exist in type '{ckTypeId}'.");
    }

    internal static Exception NotComparable(object? key)
    {
        return new RuntimeRepositoryException($"Key '{key}' is not comparable.");
    }

    internal static Exception BinaryWithFilenameNotFound(string filename, BinaryType binaryType)
    {
        return new RuntimeRepositoryException(
            $"Binary with filename '{filename}' and binary type '{binaryType}' not found.");
    }

    internal static Exception BinaryWithIdNotFound(OctoObjectId largeBinaryId)
    {
        return new RuntimeRepositoryException($"Binary with id '{largeBinaryId}' not found.");
    }

    internal static Exception BinaryContentWithIdNotFound(OctoObjectId largeBinaryId)
    {
        return new RuntimeRepositoryException($"Binary content with id '{largeBinaryId}' not found.");
    }

    internal static Exception StreamDataIsMissing(RtEntityId rtEntityId)
    {
        return new RuntimeRepositoryException(
            $"Stream data is missing for entity with id '{rtEntityId}'.");
    }

    internal static Exception InvalidValueType(object value, Type type)
    {
        return new RuntimeRepositoryException(
            $"Value '{value}' of type '{value.GetType().FullName}' is not valid for type '{type.FullName}'.");
    }

    internal static Exception FieldFilterCriteriaNestedFiltersUnsupported(Type type, FieldFilterCriteria fieldFilterCriteria)
    {
        return new RuntimeRepositoryException(
            $"Field filter criteria with nested filters is not supported for type '{type.FullName}'." +
            $" Field filter criteria: {fieldFilterCriteria}");
    }
}