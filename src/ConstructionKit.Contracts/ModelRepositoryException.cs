namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception during model repository operations
/// </summary>
public class ModelRepositoryException : CkModelException
{
    /// <inheritdoc />
    private ModelRepositoryException()
    {
    }

    /// <inheritdoc />
    private ModelRepositoryException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    private ModelRepositoryException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception ModelNotFoundInRepositories(CkModelId ckModelId)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' not found in one of the defined model repositories.");
    }

    /// <summary>
    ///     Creates an exception that indicates that a model was not found in a specific repository.
    /// </summary>
    /// <param name="ckModelId"></param>
    /// <param name="repositoryName"></param>
    /// <returns></returns>
    public static Exception ModelNotFound(CkModelId ckModelId, string repositoryName)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' not found in repository '{repositoryName}'.");
    }

    internal static Exception ErrorDuringModelLoad(CkModelId ckModelId, string repositoryName, OperationResult operationResult)
    {
        return new ModelRepositoryException(
            $"Error loading model '{ckModelId}' from repository '{repositoryName}'.{Environment.NewLine}{operationResult.GetMessages()}");
    }

    internal static Exception ModelAlreadyExists(CkModelId ckModelId, string repositoryName)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' already exists in repository '{repositoryName}'.");
    }

    internal static Exception ModelRepositoryNotFound(string repositoryName)
    {
        return new ModelRepositoryException($"Model repository '{repositoryName}' not found.");
    }

    internal static Exception ModelRepositoryNotWritable(string repositoryName)
    {
        return new ModelRepositoryException($"Model repository '{repositoryName}' is not writable.");
    }

    internal static Exception ModelRepositoryDoesNotSupportSourceIdentifier(string repositoryName)
    {
        return new ModelRepositoryException($"Model repository '{repositoryName}' does not support source identifier.");
    }

    internal static Exception DownloadError(CkModelId modelId, string repositoryName)
    {
        return new ModelRepositoryException($"Error downloading model '{modelId}' from repository '{repositoryName}'.");
    }

    internal static Exception PublishFailed(CkModelId modelId, string repositoryName, Exception exception)
    {
        return new ModelRepositoryException($"Publishing model '{modelId}' to repository '{repositoryName}' failed.", exception);
    }
}