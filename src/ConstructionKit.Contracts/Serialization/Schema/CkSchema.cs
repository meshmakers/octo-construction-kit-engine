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

    static CkSchema()
    {
        // Register sub-schemas first so they are available for $ref resolution
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "construction-kit-elements-attribute.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "construction-kit-elements-type.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "construction-kit-elements-associationRole.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "construction-kit-elements-record.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "construction-kit-elements-enum.schema")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "construction-kit-model-config.schema")));
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
        var elementsSchema = GetSchema(string.Format(SchemaPath, "construction-kit-elements.schema"));
        return elementsSchema.Bundle();
    }

    private static JsonSchema CreateBundledMetaSchema()
    {
        var metaSchemaInternal = GetSchema(string.Format(SchemaPath, "construction-kit-meta.schema"));
        return metaSchemaInternal.Bundle();
    }

    private static JsonSchema CreateBundledModelConfigSchema()
    {
        var modelConfigSchemaInternal = GetSchema(string.Format(SchemaPath, "construction-kit-model-config.schema"));
        return modelConfigSchemaInternal.Bundle();
    }

    private static JsonSchema CreateBundledCompiledModelSchema()
    {
        var compiledModelSchemaInternal = GetSchema(string.Format(SchemaPath, "construction-kit-compiled.schema"));
        return compiledModelSchemaInternal.Bundle();
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