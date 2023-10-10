using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Throws when an error occurs in a runtime repository
/// </summary>
public class RuntimeRepositoryException : PersistenceException
{
    /// <inheritdoc />
    private RuntimeRepositoryException(string message): base(message)
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
    /// The <see cref="OperationResult"/> that caused the exception
    /// </summary>
    public OperationResult OperationResult { get; }


    internal static Exception CkTypeIdMissingForType(Type type)
    {
        return new RuntimeRepositoryException($"No Construction Kit Id for type '{type.FullName}'" +
                                              $" is defined. Is attribute '{typeof(CkIdAttribute).FullName}' missing?");
    }

    internal static Exception CkTypeIdDoesNotExistInCache(CkId<CkTypeId> ckTypeId)
    {
        return new RuntimeRepositoryException($"Construction Kit Id '{ckTypeId}' was not found in model cache." +
                                              " Wrong CkTypeId used?");
    }
    
    internal static Exception EntityAlreadyAdded(string tenantId, OctoObjectId documentRtId)
    {
        return new RuntimeRepositoryException($"Entity '{documentRtId}' already added to tenant '{tenantId}'.");
    }

    internal static Exception CkTypeIdIsAbstract(string tenantId, CkId<CkTypeId> ckTypeId)
    {
        return new RuntimeRepositoryException($"CkTypeId '{ckTypeId}' is abstract in tenant '{tenantId}'.");
    }

    internal static void ThrowIfOperationResultError(OperationResult operationResult)
    {
        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            throw new RuntimeRepositoryException("Operation result contains errors.", operationResult);
        }
    }
}