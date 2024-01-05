using System.Text.Json;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization.Schema;

/// <summary>
///     Manages the runtime schemas
/// </summary>
public static class RtSchema
{
    private const string SchemaPath = "Meshmakers.Octo.Runtime.Contracts.Serialization.Schema.{0}.json";

    static RtSchema()
    {
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "runtime-element-attribute")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "runtime-element-entity")));
        SchemaRegistry.Global.Register(GetSchema(string.Format(SchemaPath, "runtime-element-association")));
    }

    /// <summary>
    ///     Returns the runtime schema
    /// </summary>
    public static JsonSchema GetRuntimeSchema()
    {
        var runtimeSchemaInternal = GetSchema(string.Format(SchemaPath, "runtime-model"));
        return runtimeSchemaInternal.Bundle();
    }

    private static JsonSchema GetSchema(string resourcesStreamPath)
    {
        var assembly = typeof(IRtSerializer).Assembly;
        var resourcesStream = assembly.GetManifestResourceStream(resourcesStreamPath);
        if (resourcesStream == null)
        {
            throw new ModelValidationException($"Resource with path '{resourcesStreamPath}' not found in assembly '{assembly.FullName}'.");
        }

        return JsonSerializer.Deserialize<JsonSchema>(resourcesStream) ??
               throw new ModelValidationException($"Could not deserialize schema '{resourcesStreamPath}'.");
    }
}