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
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-attribute")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-type")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-associationRole")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-record")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-enum")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-model-config")));
    }

    /// <summary>
    ///     Returns the construction kit elements schema
    /// </summary>
    public static JsonSchema GetElementsSchema()
    {
        var elementsSchema = GetSchema(string.Format(SchemaPath, "ck-elements"));
        return elementsSchema.Bundle();
    }

    /// <summary>
    ///     Returns the construction kit meta schema
    /// </summary>
    public static JsonSchema GetMetaSchema()
    {
        var metaSchemaInternal = GetSchema(string.Format(SchemaPath, "ck-meta"));
        return metaSchemaInternal.Bundle();
    }

    /// <summary>
    ///     Returns the construction kit model configuration file schema
    /// </summary>
    public static JsonSchema GetModelConfigSchema()
    {
        var metaSchemaInternal = GetSchema(string.Format(SchemaPath, "ck-model-config"));
        return metaSchemaInternal.Bundle();
    }
    
    /// <summary>
    ///     Returns the construction kit compiled model schema
    /// </summary>
    public static JsonSchema GetCompiledModelSchema()
    {
        var compiledModelSchemaInternal = GetSchema(string.Format(SchemaPath, "ck-compiled-model"));
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