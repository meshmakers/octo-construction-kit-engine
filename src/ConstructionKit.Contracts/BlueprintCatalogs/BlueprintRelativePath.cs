namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Helpers for working with the relative-path arguments passed to
/// <see cref="IBlueprintCatalog.OpenBlueprintFileAsync" />. Centralised here so every catalog
/// implementation enforces the same path-safety rules (no <c>..</c>, no rooted paths, normalised
/// to forward slashes).
/// </summary>
public static class BlueprintRelativePath
{
    /// <summary>
    /// Validates that <paramref name="relativePath" /> stays inside the blueprint root and returns
    /// the normalised forward-slash form. Throws <see cref="BlueprintCatalogException" /> on any
    /// traversal / rooted / empty path.
    /// </summary>
    /// <param name="relativePath">The candidate relative path</param>
    /// <returns>The normalised path (forward slashes, no leading separators).</returns>
    public static string Validate(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw BlueprintCatalogException.InvalidBlueprintRelativePath(relativePath ?? "<null>");
        }

        var normalised = relativePath.Replace('\\', '/').TrimStart('/');

        if (string.IsNullOrWhiteSpace(normalised) || Path.IsPathRooted(relativePath))
        {
            throw BlueprintCatalogException.InvalidBlueprintRelativePath(relativePath);
        }

        var segments = normalised.Split('/');
        foreach (var segment in segments)
        {
            if (segment == ".." || segment == ".")
            {
                throw BlueprintCatalogException.InvalidBlueprintRelativePath(relativePath);
            }
        }

        return normalised;
    }
}
