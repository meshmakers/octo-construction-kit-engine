using System.Text.Json;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;

/// <summary>
///     Manages the construction kit schemas
/// </summary>
public static class CkSchema
{
    private const string SchemaPath = "Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema.{0}.json";

    static CkSchema()
    {
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
    public static JsonSchema GetElementsSchema()
    {
        var elementsSchema = GetSchema(string.Format(SchemaPath, "construction-kit-elements.schema"));
        return elementsSchema.Bundle();
    }

    /// <summary>
    ///     Returns the construction kit meta schema
    /// </summary>
    public static JsonSchema GetMetaSchema()
    {
        var metaSchemaInternal = GetSchema(string.Format(SchemaPath, "construction-kit-meta.schema"));
        return metaSchemaInternal.Bundle();
    }

    /// <summary>
    ///     Returns the construction kit model configuration file schema
    /// </summary>
    public static JsonSchema GetModelConfigSchema()
    {
        var metaSchemaInternal = GetSchema(string.Format(SchemaPath, "construction-kit-model-config.schema"));
        return metaSchemaInternal.Bundle();
    }
    
    /// <summary>
    ///     Returns the construction kit compiled model schema
    /// </summary>
    public static JsonSchema GetCompiledModelSchema()
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