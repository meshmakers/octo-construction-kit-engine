using System.Text.Json;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;

/// <summary>
///     Manages the construction kit schemas
/// </summary>
public static class CkSchema
{
    private const string SchemaPath = "Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema.{0}.json";

    // Cached bundled schemas - created once to avoid race conditions with SchemaRegistry.Global
    private static readonly Lazy<JsonSchema> ElementsSchemaLazy = new(CreateBundledElementsSchema);
    private static readonly Lazy<JsonSchema> MetaSchemaLazy = new(CreateBundledMetaSchema);
    private static readonly Lazy<JsonSchema> ModelConfigSchemaLazy = new(CreateBundledModelConfigSchema);
    private static readonly Lazy<JsonSchema> CompiledModelSchemaLazy = new(CreateBundledCompiledModelSchema);

    // Cache sub-schemas to ensure they are loaded (and auto-registered) before main schemas need them
    private static readonly Lazy<JsonSchema> AttributeSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "construction-kit-elements-attribute.schema")));
    private static readonly Lazy<JsonSchema> TypeSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "construction-kit-elements-type.schema")));
    private static readonly Lazy<JsonSchema> AssociationRoleSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "construction-kit-elements-associationRole.schema")));
    private static readonly Lazy<JsonSchema> RecordSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "construction-kit-elements-record.schema")));
    private static readonly Lazy<JsonSchema> EnumSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "construction-kit-elements-enum.schema")));

    /// <summary>
    ///     Ensures all sub-schemas are loaded and registered before using main schemas.
    ///     In JsonSchema.Net 8.0, schemas are auto-registered when deserialized.
    /// </summary>
    private static void EnsureSubSchemasLoaded()
    {
        // Accessing .Value triggers lazy loading (and auto-registration)
        _ = AttributeSchemaLazy.Value;
        _ = TypeSchemaLazy.Value;
        _ = AssociationRoleSchemaLazy.Value;
        _ = RecordSchemaLazy.Value;
        _ = EnumSchemaLazy.Value;
    }

    /// <summary>
    ///     Returns the construction kit elements schema
    /// </summary>
    public static JsonSchema GetElementsSchema() => ElementsSchemaLazy.Value;

    /// <summary>
    ///     Returns the construction kit meta schema
    /// </summary>
    public static JsonSchema GetMetaSchema() => MetaSchemaLazy.Value;

    /// <summary>
    ///     Returns the construction kit model configuration file schema
    /// </summary>
    public static JsonSchema GetModelConfigSchema() => ModelConfigSchemaLazy.Value;

    /// <summary>
    ///     Returns the construction kit compiled model schema
    /// </summary>
    public static JsonSchema GetCompiledModelSchema() => CompiledModelSchemaLazy.Value;

    private static JsonSchema CreateBundledElementsSchema()
    {
        // Note: Bundle() was removed in JsonSchema.Net 8.0. Sub-schemas are auto-registered
        // when deserialized, so we just need to ensure they're loaded before the main schema.
        EnsureSubSchemasLoaded();
        return GetSchema(string.Format(SchemaPath, "construction-kit-elements.schema"));
    }

    private static JsonSchema CreateBundledMetaSchema()
    {
        EnsureSubSchemasLoaded();
        return GetSchema(string.Format(SchemaPath, "construction-kit-meta.schema"));
    }

    private static JsonSchema CreateBundledModelConfigSchema()
    {
        EnsureSubSchemasLoaded();
        return GetSchema(string.Format(SchemaPath, "construction-kit-model-config.schema"));
    }

    private static JsonSchema CreateBundledCompiledModelSchema()
    {
        EnsureSubSchemasLoaded();
        return GetSchema(string.Format(SchemaPath, "construction-kit-compiled.schema"));
    }

    private static JsonSchema GetSchema(string resourcesStreamPath)
    {
        var assembly = typeof(ICkSerializer).Assembly;
        var resourcesStream = assembly.GetManifestResourceStream(resourcesStreamPath);
        if (resourcesStream == null)
        {
            throw new ModelValidationException($"Resource with path '{resourcesStreamPath}' not found in assembly '{assembly.FullName}'.");
        }

        return JsonSerializer.Deserialize<JsonSchema>(resourcesStream) ??
               throw new ModelValidationException($"Could not deserialize schema '{resourcesStreamPath}'.");
    }
}