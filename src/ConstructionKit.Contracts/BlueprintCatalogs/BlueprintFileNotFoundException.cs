namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Thrown when a file referenced by relative path does not exist inside a known blueprint. Subtype of
/// <see cref="BlueprintCatalogException" /> so soft-not-found call sites can pattern-match on the
/// type without inspecting the message string.
/// </summary>
public class BlueprintFileNotFoundException : BlueprintCatalogException
{
    /// <summary>
    /// The blueprint that was located but missed the requested file.
    /// </summary>
    public BlueprintId BlueprintId { get; }

    /// <summary>
    /// Catalog that served the blueprint metadata but could not locate the file.
    /// </summary>
    public string CatalogName { get; }

    /// <summary>
    /// Relative path of the missing file inside the blueprint's folder.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Creates a new <see cref="BlueprintFileNotFoundException" />.
    /// </summary>
    public BlueprintFileNotFoundException(BlueprintId blueprintId, string catalogName, string relativePath)
        : base($"File '{relativePath}' was not found inside blueprint '{blueprintId}' in catalog '{catalogName}'.")
    {
        BlueprintId = blueprintId;
        CatalogName = catalogName;
        RelativePath = relativePath;
    }
}
