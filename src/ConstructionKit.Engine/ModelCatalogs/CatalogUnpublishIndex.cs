using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// AB#5433: the index changes of an unpublish, as pure functions on the JSON text of the index files, shared by
/// the GitHub and the local file system catalog (like the publish-side merges). Each function returns the new file
/// content, or <c>null</c> when nothing has to change.
/// </summary>
internal static class CatalogUnpublishIndex
{
    public const string CatalogFileName = "catalog.json";
    public const string WithdrawnFileName = "withdrawn.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary><c>&lt;root&gt;&lt;letter&gt;/&lt;Name&gt;/withdrawn.json</c></summary>
    public static string WithdrawnFilePath(string root, string modelName) =>
        $"{CkCatalogLayout.ModelDirectory(root, modelName)}{WithdrawnFileName}";

    /// <summary><c>&lt;root&gt;&lt;letter&gt;/&lt;Name&gt;/&lt;major&gt;/catalog.json</c></summary>
    public static string VersionsCatalogPath(string root, CkModelId modelId) =>
        $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{modelId.Version.Major}/{CatalogFileName}";

    /// <summary><c>&lt;root&gt;&lt;letter&gt;/&lt;Name&gt;/catalog.json</c></summary>
    public static string LibraryCatalogPath(string root, string modelName) =>
        $"{CkCatalogLayout.ModelDirectory(root, modelName)}{CatalogFileName}";

    /// <summary><c>&lt;root&gt;catalog.json</c></summary>
    public static string RootCatalogPath(string root) => $"{root}{CatalogFileName}";

    /// <summary>True when the per-major index lists the exact version.</summary>
    public static bool ListsVersion(string? versionsJson, CkModelId modelId)
    {
        var catalog = Deserialize<SharedCatalogTypes.ModelLibraryVersionsCatalog>(versionsJson);
        var version = modelId.Version.ToString();
        return catalog != null && catalog.Versions.Any(v => v.Version == version);
    }

    /// <summary>The withdrawn record of the version, or null.</summary>
    public static SharedCatalogTypes.WithdrawnVersionEntry? FindWithdrawn(string? withdrawnJson, CkVersion version)
    {
        var catalog = Deserialize<SharedCatalogTypes.WithdrawnVersionsCatalog>(withdrawnJson);
        var text = version.ToString();
        return catalog?.Withdrawn.FirstOrDefault(w => w.Version == text);
    }

    /// <summary>Adds the burned-version record; null when the version is recorded already.</summary>
    public static string? AddWithdrawn(string? withdrawnJson, CkModelId modelId, string? reason)
    {
        var catalog = Deserialize<SharedCatalogTypes.WithdrawnVersionsCatalog>(withdrawnJson) ??
                      new SharedCatalogTypes.WithdrawnVersionsCatalog { ModelId = modelId.Name };
        var text = modelId.Version.ToString();
        if (catalog.Withdrawn.Any(w => w.Version == text))
        {
            return null;
        }

        catalog.Withdrawn.Add(new SharedCatalogTypes.WithdrawnVersionEntry
        {
            Version = text,
            WithdrawnAt = DateTime.UtcNow,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim()
        });
        catalog.Withdrawn = catalog.Withdrawn.OrderBy(w => new CkVersion(w.Version)).ToList();
        catalog.UpdatedAt = DateTime.UtcNow;
        return Serialize(catalog);
    }

    /// <summary>
    /// Removes the version from the per-major index and recomputes the latest version. <paramref name="majorEmpty" />
    /// tells whether the major has no version left afterwards (the caller then prunes the library index and deletes
    /// this file). Null when the version is not listed.
    /// </summary>
    public static string? RemoveVersion(string? versionsJson, CkModelId modelId, out bool majorEmpty)
    {
        var catalog = Deserialize<SharedCatalogTypes.ModelLibraryVersionsCatalog>(versionsJson);
        if (catalog == null)
        {
            majorEmpty = true;
            return null;
        }

        var text = modelId.Version.ToString();
        var removed = catalog.Versions.RemoveAll(v => v.Version == text) > 0;
        majorEmpty = catalog.Versions.Count == 0;
        if (!removed)
        {
            return null;
        }

        catalog.LatestVersion = catalog.Versions
            .OrderByDescending(v => new CkVersion(v.Version))
            .FirstOrDefault()?.Version;
        catalog.UpdatedAt = DateTime.UtcNow;
        return Serialize(catalog);
    }

    /// <summary>
    /// Removes the major from the model's library index. <paramref name="modelEmpty" /> tells whether the model has
    /// no major left (the caller then prunes the root index and deletes this file). Null when it is not listed.
    /// </summary>
    public static string? RemoveMajor(string? libraryJson, int major, out bool modelEmpty)
    {
        var catalog = Deserialize<SharedCatalogTypes.ModelLibraryCatalog>(libraryJson);
        if (catalog == null)
        {
            modelEmpty = true;
            return null;
        }

        var removed = catalog.MajorVersions.RemoveAll(m => m.MajorVersion == major) > 0;
        modelEmpty = catalog.MajorVersions.Count == 0;
        if (!removed)
        {
            return null;
        }

        catalog.UpdatedAt = DateTime.UtcNow;
        return Serialize(catalog);
    }

    /// <summary>Removes the model from the root index; null when it is not listed.</summary>
    public static string? RemoveModel(string? rootJson, string modelName)
    {
        var catalog = Deserialize<SharedCatalogTypes.RootCatalog>(rootJson);
        if (catalog == null || catalog.Models.RemoveAll(m => m.ModelName == modelName) == 0)
        {
            return null;
        }

        catalog.UpdatedAt = DateTime.UtcNow;
        return Serialize(catalog);
    }

    private static T? Deserialize<T>(string? json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json!, ReadOptions);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions);
}
