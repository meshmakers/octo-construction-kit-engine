using System.Text.Json;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization.Schema;

/// <summary>
///     Manages the blueprint schemas
/// </summary>
public static class BlueprintSchema
{
    private const string SchemaPath = "Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization.Schema.{0}.json";

    // Cached bundled schemas - created once to avoid race conditions with SchemaRegistry.Global
    private static readonly Lazy<JsonSchema> MetaSchemaLazy = new(CreateBundledMetaSchema);
    private static readonly Lazy<JsonSchema> CatalogIndexSchemaLazy = new(CreateBundledCatalogIndexSchema);
    private static readonly Lazy<JsonSchema> LibraryVersionsSchemaLazy = new(CreateBundledLibraryVersionsSchema);
    private static readonly Lazy<JsonSchema> MigrationSchemaLazy = new(CreateBundledMigrationSchema);
    private static readonly Lazy<JsonSchema> CacheSchemaLazy = new(CreateBundledCacheSchema);

    // Note: In JsonSchema.Net 8.0, schemas are automatically registered in SchemaRegistry.Global
    // when deserialized. Manual registration is no longer needed and would cause duplicate registration errors.

    /// <summary>
    ///     Returns the blueprint meta schema
    /// </summary>
    public static JsonSchema GetMetaSchema() => MetaSchemaLazy.Value;

    /// <summary>
    ///     Returns the blueprint catalog index schema
    /// </summary>
    public static JsonSchema GetCatalogIndexSchema() => CatalogIndexSchemaLazy.Value;

    /// <summary>
    ///     Returns the blueprint library versions schema
    /// </summary>
    public static JsonSchema GetLibraryVersionsSchema() => LibraryVersionsSchemaLazy.Value;

    /// <summary>
    ///     Returns the blueprint migration schema
    /// </summary>
    public static JsonSchema GetMigrationSchema() => MigrationSchemaLazy.Value;

    /// <summary>
    ///     Returns the schema for the build-time embedded-blueprint cache file produced by the
    ///     BlueprintEmbed MSBuild task and consumed by the BlueprintSourceGenerator.
    /// </summary>
    public static JsonSchema GetCacheSchema() => CacheSchemaLazy.Value;

    private static JsonSchema CreateBundledMetaSchema()
    {
        // Note: Bundle() was removed in JsonSchema.Net 8.0. Since sub-schemas are registered
        // in SchemaRegistry.Global (in the static constructor), $ref resolution works without bundling.
        return GetSchema(string.Format(SchemaPath, "blueprint-meta.schema"));
    }

    private static JsonSchema CreateBundledCatalogIndexSchema()
    {
        return GetSchema(string.Format(SchemaPath, "blueprint-catalog-index.schema"));
    }

    private static JsonSchema CreateBundledLibraryVersionsSchema()
    {
        return GetSchema(string.Format(SchemaPath, "blueprint-library-versions.schema"));
    }

    private static JsonSchema CreateBundledMigrationSchema()
    {
        return GetSchema(string.Format(SchemaPath, "blueprint-migration.schema"));
    }

    private static JsonSchema CreateBundledCacheSchema()
    {
        return GetSchema(string.Format(SchemaPath, "blueprints-cache.schema"));
    }

    private static JsonSchema GetSchema(string resourcesStreamPath)
    {
        var assembly = typeof(BlueprintSchema).Assembly;
        var resourcesStream = assembly.GetManifestResourceStream(resourcesStreamPath);
        if (resourcesStream == null)
        {
            throw new BlueprintCatalogException($"Resource with path '{resourcesStreamPath}' not found in assembly '{assembly.FullName}'.");
        }

        return JsonSerializer.Deserialize<JsonSchema>(resourcesStream) ??
               throw new BlueprintCatalogException($"Could not deserialize schema '{resourcesStreamPath}'.");
    }
}
