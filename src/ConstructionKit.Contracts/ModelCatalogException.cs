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

    /// <summary>
    /// True when the exception says that a model is not in the catalog (as opposed to the catalog not being readable).
    /// </summary>
    public bool IsModelNotFound { get; private init; }

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

    internal static Exception UnpublishFailed(CkModelId modelId, string catalogName, Exception exception)
    {
        return new ModelCatalogException(
            $"Unpublishing model '{modelId}' from catalog '{catalogName}' failed: {exception.Message}", exception);
    }

    /// <summary>
    ///     AB#5433: the version number was unpublished earlier and is burned — it is never published again, because
    ///     tenants and caches may hold the old file.
    /// </summary>
    internal static Exception VersionWithdrawn(CkModelId modelId, string catalogName, string? reason,
        DateTime? withdrawnAt)
    {
        var detail = withdrawnAt == null ? "" : $" on {withdrawnAt:yyyy-MM-dd}";
        var because = string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";
        return new ModelCatalogException(
            $"Version '{modelId}' was unpublished from catalog '{catalogName}'{detail}{because}. A withdrawn version " +
            "number is burned and is never published again; publish the next free version number instead.");
    }

    /// <summary>
    /// AB#6581: the source of truth of a catalog could not be read, so a decision that needs the whole catalog (the
    /// unpublish dependents check) is not taken. Same rule and code as the publish gate (<c>OCTO-CK202</c>).
    /// </summary>
    internal static Exception CatalogUnreachable(string catalogName, string? reason)
    {
        var because = string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}";
        return new ModelCatalogException(
            $"OCTO-CK202: catalog '{catalogName}' could not be read{because}. Nothing was changed. Check network " +
            "connectivity, the catalog repository and the access token and retry.");
    }

    /// <summary>AB#5433: unpublish is refused while published models still depend on the version.</summary>
    internal static Exception UnpublishRefusedDependents(CkModelId modelId, string catalogName,
        IReadOnlyList<ModelCatalogs.UnpublishDependent> dependents)
    {
        var list = string.Join(", ", dependents.Select(d => $"{d.ModelId} (needs {d.Range})"));
        return new ModelCatalogException(
            $"Cannot unpublish '{modelId}' from catalog '{catalogName}': published models still depend on it and would " +
            $"be left with an unresolvable dependency: {list}. Unpublish or replace the dependents first.");
    }

    /// <summary>AB#5433: the public catalog needs a reason for every unpublish.</summary>
    internal static Exception UnpublishReasonRequired(string catalogName)
    {
        return new ModelCatalogException(
            $"Unpublishing from the public catalog '{catalogName}' requires a reason (--reason); it is written to the " +
            "commit message of the catalog change.");
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
        return new ModelCatalogException($"Model '{ckModelId}' not found in catalog '{catalogName}'.")
            { IsModelNotFound = true };
    }

    internal static Exception ModelCatalogNotFound(string catalogName)
    {
        return new ModelCatalogException($"Model catalog '{catalogName}' not found.");
    }

    internal static Exception ModelNotFoundInCatalogs(CkModelId ckModelId)
    {
        return new ModelCatalogException($"Model '{ckModelId}' not found in one of the registered catalogs.")
            { IsModelNotFound = true };
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
