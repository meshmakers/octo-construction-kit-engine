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
        return new ModelRepositoryException($"Model '{ckModelId}' not found in one of the registered model repositories.");
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

    internal static Exception CustomizationNotSupported(string repositoryName)
    {
        return new ModelRepositoryException($"Customization is not supported by repository '{repositoryName}'.");
    }


}