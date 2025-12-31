namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Exception thrown when a blueprint catalog operation fails
/// </summary>
public class BlueprintCatalogException : Exception
{
    /// <summary>
    /// Creates a new instance of <see cref="BlueprintCatalogException"/>
    /// </summary>
    /// <param name="message">The exception message</param>
    public BlueprintCatalogException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintCatalogException"/>
    /// </summary>
    /// <param name="message">The exception message</param>
    /// <param name="innerException">The inner exception</param>
    public BlueprintCatalogException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception for a blueprint not found error
    /// </summary>
    /// <param name="blueprintId">The blueprint id that was not found</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException BlueprintNotFound(BlueprintId blueprintId)
    {
        return new BlueprintCatalogException($"Blueprint '{blueprintId}' was not found in any catalog.");
    }

    /// <summary>
    /// Creates an exception for a blueprint not found in catalog error
    /// </summary>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="blueprintId">The blueprint id that was not found</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException BlueprintNotFoundInCatalog(string catalogName, BlueprintId blueprintId)
    {
        return new BlueprintCatalogException($"Blueprint '{blueprintId}' was not found in catalog '{catalogName}'.");
    }

    /// <summary>
    /// Creates an exception for a catalog not found error
    /// </summary>
    /// <param name="catalogName">The catalog name that was not found</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException CatalogNotFound(string catalogName)
    {
        return new BlueprintCatalogException($"Catalog '{catalogName}' was not found.");
    }

    /// <summary>
    /// Creates an exception for a catalog read error
    /// </summary>
    /// <param name="catalogName">The catalog name</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException CatalogCannotRead(string catalogName)
    {
        return new BlueprintCatalogException($"Catalog '{catalogName}' cannot be read.");
    }

    /// <summary>
    /// Creates an exception for a catalog write error
    /// </summary>
    /// <param name="catalogName">The catalog name</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException CatalogCannotWrite(string catalogName)
    {
        return new BlueprintCatalogException($"Catalog '{catalogName}' cannot be written to.");
    }

    /// <summary>
    /// Creates an exception for a blueprint already exists error
    /// </summary>
    /// <param name="blueprintId">The blueprint id that already exists</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException BlueprintAlreadyExists(BlueprintId blueprintId)
    {
        return new BlueprintCatalogException($"Blueprint '{blueprintId}' already exists. Use force option to overwrite.");
    }

    /// <summary>
    /// Creates an exception for a circular blueprint reference error
    /// </summary>
    /// <param name="blueprintId">The blueprint id causing the circular reference</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException CircularBlueprintReference(BlueprintId blueprintId)
    {
        return new BlueprintCatalogException($"Circular reference detected for blueprint '{blueprintId}'.");
    }

    /// <summary>
    /// Creates an exception for an invalid GitHub repository error
    /// </summary>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="gitHubPagesUri">The GitHub Pages URI</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException InvalidGitHubRepository(string catalogName, string gitHubPagesUri)
    {
        return new BlueprintCatalogException(
            $"Failed to access GitHub repository for catalog '{catalogName}' at '{gitHubPagesUri}'.");
    }

    /// <summary>
    /// Creates an exception for a request timeout error
    /// </summary>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="gitHubPagesUri">The GitHub Pages URI</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException RequestTimeout(string catalogName, string gitHubPagesUri)
    {
        return new BlueprintCatalogException(
            $"Request to GitHub repository for catalog '{catalogName}' at '{gitHubPagesUri}' timed out.");
    }

    /// <summary>
    /// Creates an exception for a missing GitHub Pages URI error
    /// </summary>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException GitHubPagesUriMissing()
    {
        return new BlueprintCatalogException("GitHub Pages URI is not configured.");
    }

    /// <summary>
    /// Creates an exception for an error during blueprint load
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="operationResult">The operation result with errors</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException ErrorDuringBlueprintLoad(
        BlueprintId blueprintId,
        string catalogName,
        OperationResult operationResult)
    {
        var errors = string.Join(", ", operationResult.Messages.Select(m => m.MessageText));
        return new BlueprintCatalogException(
            $"Error loading blueprint '{blueprintId}' from catalog '{catalogName}': {errors}");
    }

    /// <summary>
    /// Creates an exception for a missing GitHub API token error
    /// </summary>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException GitHubTokenMissing()
    {
        return new BlueprintCatalogException("GitHub API token is not configured. Use 'config' command to set it.");
    }

    /// <summary>
    /// Creates an exception for a publish failure
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="innerException">The inner exception</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException PublishFailed(BlueprintId blueprintId, string catalogName, Exception innerException)
    {
        return new BlueprintCatalogException(
            $"Failed to publish blueprint '{blueprintId}' to catalog '{catalogName}': {innerException.Message}",
            innerException);
    }

    /// <summary>
    /// Creates an exception for when an existing version catalog cannot be read
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="catalogPath">The catalog path</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException CannotReadExistingVersionsCatalog(
        BlueprintId blueprintId,
        string catalogName,
        string catalogPath)
    {
        return new BlueprintCatalogException(
            $"Cannot read existing versions catalog for blueprint '{blueprintId}' in catalog '{catalogName}' at path '{catalogPath}'.");
    }

    /// <summary>
    /// Creates an exception for when an existing library catalog cannot be read
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="catalogName">The catalog name</param>
    /// <param name="catalogPath">The catalog path</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException CannotReadExistingLibraryCatalog(
        BlueprintId blueprintId,
        string catalogName,
        string catalogPath)
    {
        return new BlueprintCatalogException(
            $"Cannot read existing library catalog for blueprint '{blueprintId}' in catalog '{catalogName}' at path '{catalogPath}'.");
    }

    /// <summary>
    /// Creates an exception for a blueprint already exists in catalog error
    /// </summary>
    /// <param name="blueprintId">The blueprint id that already exists</param>
    /// <param name="catalogName">The catalog name</param>
    /// <returns>A new exception instance</returns>
    public static BlueprintCatalogException BlueprintAlreadyExistsInCatalog(BlueprintId blueprintId, string catalogName)
    {
        return new BlueprintCatalogException(
            $"Blueprint '{blueprintId}' already exists in catalog '{catalogName}'. Use force option to overwrite.");
    }
}
