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

    static BlueprintSchema()
    {
        // Register sub-schemas first so they are available for $ref resolution
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "blueprint-catalog-index.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "blueprint-library-versions.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "blueprint-migration.schema")));
    }

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

    private static JsonSchema CreateBundledMetaSchema()
    {
        var metaSchemaInternal = GetSchema(string.Format(SchemaPath, "blueprint-meta.schema"));
        return metaSchemaInternal.Bundle();
    }

    private static JsonSchema CreateBundledCatalogIndexSchema()
    {
        var catalogIndexSchemaInternal = GetSchema(string.Format(SchemaPath, "blueprint-catalog-index.schema"));
        return catalogIndexSchemaInternal.Bundle();
    }

    private static JsonSchema CreateBundledLibraryVersionsSchema()
    {
        var libraryVersionsSchemaInternal = GetSchema(string.Format(SchemaPath, "blueprint-library-versions.schema"));
        return libraryVersionsSchemaInternal.Bundle();
    }

    private static JsonSchema CreateBundledMigrationSchema()
    {
        var migrationSchemaInternal = GetSchema(string.Format(SchemaPath, "blueprint-migration.schema"));
        return migrationSchemaInternal.Bundle();
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
