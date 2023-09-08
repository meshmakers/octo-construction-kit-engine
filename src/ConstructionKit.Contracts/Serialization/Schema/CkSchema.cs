using System.Text.Json;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;

/// <summary>
/// Manages the construction kit schemas
/// </summary>
public static class CkSchema
{
    private static readonly JsonSchema ElementsSchemaInternal;
    private static readonly JsonSchema MetaSchemaInternal;
    private static readonly JsonSchema CompiledModelSchemaInternal;
    private const string SchemaPath = "Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema.{0}.json";

    static CkSchema()
    {
        ElementsSchemaInternal = GetSchema(string.Format(SchemaPath, "ck-elements"));
        MetaSchemaInternal = GetSchema(string.Format(SchemaPath, "ck-meta"));
        CompiledModelSchemaInternal = GetSchema(string.Format(SchemaPath, "ck-compiled-model"));
        SchemaRegistry.Global.Register(ElementsSchemaInternal);
        SchemaRegistry.Global.Register(MetaSchemaInternal);
        SchemaRegistry.Global.Register(CompiledModelSchemaInternal);
        
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-attribute")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-type")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-associationRole")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-record")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "ck-element-enum")));
    }

    /// <summary>
    /// Returns the construction kit elements schema
    /// </summary>
    public static JsonSchema ElementsSchema => ElementsSchemaInternal.Bundle();

    /// <summary>
    /// Returns the construction kit meta schema
    /// </summary>
    public static JsonSchema MetaSchema => MetaSchemaInternal.Bundle();

    /// <summary>
    /// Returns the construction kit compiled model schema
    /// </summary>
    public static JsonSchema CompiledModelSchema => CompiledModelSchemaInternal.Bundle();

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