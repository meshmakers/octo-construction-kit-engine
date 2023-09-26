using System.Text.Json;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization.Schema;

/// <summary>
/// Manages the runtime schemas
/// </summary>
public static class RtSchema
{
    private static readonly JsonSchema RuntimeSchemaInternal;
    private const string SchemaPath = "Meshmakers.Octo.Runtime.Contracts.Serialization.Schema.{0}.json";

    static RtSchema()
    {
        RuntimeSchemaInternal = GetSchema(string.Format(SchemaPath, "runtime-model"));
        SchemaRegistry.Global.Register(RuntimeSchemaInternal);
        
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "runtime-element-attribute")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "runtime-element-entity")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "runtime-element-association")));
    }

    /// <summary>
    /// Returns the runtime schema
    /// </summary>
    public static JsonSchema RuntimeSchema => RuntimeSchemaInternal.Bundle();

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