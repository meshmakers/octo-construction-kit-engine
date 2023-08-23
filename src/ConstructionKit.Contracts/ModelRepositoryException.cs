namespace Meshmakers.Octo.ConstructionKit.Contracts;


/// <summary>
/// Used to indicate an exception during model repository operations
/// </summary>
public class ModelRepositoryException : Exception
{
    /// <inheritdoc />
    public ModelRepositoryException()
    {
    }

    /// <inheritdoc />
    public ModelRepositoryException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public ModelRepositoryException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception ModelNotFoundInRepositories(CkModelId ckModelId)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' not found in one of the defined model repositories.");
    }
    
    internal static Exception ModelNotFound(CkModelId ckModelId, string repositoryName)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' not found in repository '{repositoryName}'.");
    }

    internal static Exception ErrorDuringModelLoad(CkModelId ckModelId, string repositoryName, OperationResult operationResult)
    {
        return new ModelRepositoryException($"Error loading model '{ckModelId}' from repository '{repositoryName}'.{Environment.NewLine}{operationResult.GetMessages()}");
    }
}
