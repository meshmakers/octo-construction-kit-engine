using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization.Schema;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     The root object of the compiled version of a CK model.
/// </summary>
[OctoJsonSchema(typeof(CkSchema), nameof(CkSchema.GetCompiledModelSchema))]
public class CkCompiledModelRoot : CkModelRootBase
{
    /// <summary>
    ///     The URI of the schema for the compiled CK model.
    /// </summary>
    public const string CkCompiledModelSchemaUri = "https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json";

    /// <summary>
    ///     The URI of the schema for the compiled CK model used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public string SchemaUri { get; } = CkCompiledModelSchemaUri;

    /// <summary>
    ///     Gets or sets the dependencies of the model.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelId>? Dependencies { get; set; }

    /// <summary>
    ///     Gets or sets the inline migration data for this compiled model.
    ///     When present, allows any service to run CK model migrations without
    ///     needing the CK model NuGet package as a reference.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkCompiledMigrationDataDto? Migrations { get; set; }
}