namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Indicates an exception during model catalog operations
/// </summary>
public class ModelCatalogException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCatalogException"/> class.
    /// </summary>
    private ModelCatalogException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCatalogException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">Error message that explains the reason for the exception.</param>
    private ModelCatalogException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCatalogException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">Error message that explains the reason for the exception.</param>
    /// <param name="inner">Inner exception that is the cause of this exception.</param>
    private ModelCatalogException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception PublishFailed(CkModelId modelId, string catalogName, Exception exception)
    {
        return new ModelCatalogException($"Publishing model '{modelId}' to catalog '{catalogName}' failed: {exception.Message}", exception);
    }

    /// <summary>
    /// A GitHub Contents API call gave up. AB#5298: Octokit's own message is the constant
    /// "An error occurred with this API request" - the status code and GitHub's error text live
    /// on properties nobody logs - so a failed publish told an operator nothing about whether it
    /// was a rate limit, a 5xx or a stale ref. Put all of it into the message the publish command
    /// prints, plus which file and how many attempts, so the next occurrence is diagnosable from
    /// the build log alone.
    /// </summary>
    internal static Exception GitHubRequestFailed(string operation, string path, int statusCode,
        string? apiErrorMessage, int attempts, Exception inner)
    {
        var detail = string.IsNullOrWhiteSpace(apiErrorMessage) ? string.Empty : $": {apiErrorMessage}";
        var tries = attempts == 1 ? "1 attempt" : $"{attempts} attempts";
        return new ModelCatalogException(
            $"GitHub {operation} of '{path}' failed after {tries} with HTTP {statusCode}{detail}", inner);
    }

    internal static Exception ModelAlreadyExists(CkModelId ckModelId, string catalogName)
    {
        return new ModelCatalogException($"Model '{ckModelId}' already exists in catalog '{catalogName}'.");
    }

    internal static Exception ErrorDuringModelLoad(CkModelId ckModelId, string catalogName, OperationResult operationResult)
    {
        return new ModelCatalogException(
            $"Error loading model '{ckModelId}' from catalog '{catalogName}'.{Environment.NewLine}{operationResult.GetMessages()}");
    }

    /// <summary>
    ///     Creates an exception that indicates that a model was not found in a specific repository.
    /// </summary>
    /// <param name="ckModelId"></param>
    /// <param name="catalogName"></param>
    /// <returns></returns>
    public static Exception ModelNotFound(CkModelId ckModelId, string catalogName)
    {
        return new ModelCatalogException($"Model '{ckModelId}' not found in catalog '{catalogName}'.");
    }

    internal static Exception ModelCatalogNotFound(string catalogName)
    {
        return new ModelCatalogException($"Model catalog '{catalogName}' not found.");
    }

    internal static Exception ModelNotFoundInCatalogs(CkModelId ckModelId)
    {
        return new ModelCatalogException($"Model '{ckModelId}' not found in one of the registered catalogs.");
    }

    internal static Exception CatalogDoesNotSupportSourceIdentifier(string catalogName)
    {
        return new ModelCatalogException($"Catalog '{catalogName}' does not support source identifier.");
    }

    internal static Exception CatalogNotWritable(string catalogName)
    {
        return new ModelCatalogException($"Catalog '{catalogName}' is not writable.");
    }

    internal static Exception GitHubTokenMissing()
    {
        return new ModelCatalogException("GitHub token is missing.");
    }

    internal static Exception GitHubPagesUriMissing()
    {
        return new ModelCatalogException("GitHub Pages URI is missing.");
    }

    internal static Exception InvalidGitHubRepository(string catalogName, string? uri)
    {
        return new ModelCatalogException(
            $"GitHub catalog '{catalogName}' is invalid, because index files are missing. Please check if GitHub Pages URI '{uri}' is a correct source.");
    }

    internal static Exception InvalidModelLibraryCatalogFile(CkModelIdVersionRange ckModelIdVersionRange, string catalogName, string? uri)
    {
        return new ModelCatalogException(
            $"Model index file for '{ckModelIdVersionRange}' in catalog '{catalogName}' is empty. Please check if GitHub Pages URI '{uri}' is a correct source.");
    }

    internal static Exception InvalidModelLibraryCatalogFile(CkModelIdVersionRange ckModelIdVersionRange, string catalogName, string? uri, Exception innerException)
    {
        return new ModelCatalogException(
            $"Model index file for '{ckModelIdVersionRange}' in repository '{catalogName}' is invalid and cannot be read. Please check if GitHub Pages URI '{uri}' is a correct source.", innerException);
    }

    internal static Exception RequestTimeoutGitHubRepository(string repositoryName, string valueGitHubPagesUri)
    {
        return new ModelCatalogException(
            $"Request to GitHub repository '{repositoryName}' at '{valueGitHubPagesUri}' timed out.");
    }

    internal static Exception CannotReadExistingModelVersionCatalog(CkModelId modelId, string catalogName, string catalogPath)
    {
        return new ModelCatalogException(
            $"Cannot read existing model version for model '{modelId}' from catalog '{catalogName}' at path '{catalogPath}'.");
    }

    internal static Exception CannotReadExistingModelLibraryCatalog(CkModelId modelId, string catalogName, string catalogPath)
    {
        return new ModelCatalogException(
            $"Cannot read existing model library for model '{modelId}' from catalog '{catalogName}' at path '{catalogPath}'.");
    }

    internal static Exception CatalogNotEnabledToRead(string catalogName)
    {
        return new ModelCatalogException(
            $"Model catalog '{catalogName}' is not enabled for read operations.");
    }

    internal static Exception CatalogNotEnabledToWrite(string catalogName)
    {
        return new ModelCatalogException(
            $"Model catalog '{catalogName}' is not enabled for write operations.");
    }
}
