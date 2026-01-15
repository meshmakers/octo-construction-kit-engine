using System.Text.Json;
using Json.Schema;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization.Schema;

/// <summary>
///     Manages the runtime schemas
/// </summary>
public static class RtSchema
{
    private const string SchemaPath = "Meshmakers.Octo.Runtime.Contracts.Serialization.Schema.{0}.schema.json";

    // Cached bundled schema - created once during static initialization to avoid race conditions
    private static readonly Lazy<JsonSchema> RuntimeSchemaLazy = new(CreateBundledRuntimeSchema);

    // Cache sub-schemas to ensure they are loaded (and auto-registered) before main schema needs them
    private static readonly Lazy<JsonSchema> AttributeSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "runtime-elements-attribute")));
    private static readonly Lazy<JsonSchema> EntitySchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "runtime-elements-entity")));
    private static readonly Lazy<JsonSchema> AssociationSchemaLazy = new(() => GetSchema(string.Format(SchemaPath, "runtime-elements-association")));

    /// <summary>
    ///     Ensures all sub-schemas are loaded and registered before using main schema.
    ///     In JsonSchema.Net 8.0, schemas are auto-registered when deserialized.
    /// </summary>
    private static void EnsureSubSchemasLoaded()
    {
        // Accessing .Value triggers lazy loading (and auto-registration)
        _ = AttributeSchemaLazy.Value;
        _ = EntitySchemaLazy.Value;
        _ = AssociationSchemaLazy.Value;
    }

    /// <summary>
    ///     Returns the runtime schema
    /// </summary>
    public static JsonSchema GetRuntimeSchema()
    {
        return RuntimeSchemaLazy.Value;
    }

    private static JsonSchema CreateBundledRuntimeSchema()
    {
        // Note: Bundle() was removed in JsonSchema.Net 8.0. Sub-schemas are auto-registered
        // when deserialized, so we just need to ensure they're loaded before the main schema.
        EnsureSubSchemasLoaded();
        return GetSchema(string.Format(SchemaPath, "runtime-model"));
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