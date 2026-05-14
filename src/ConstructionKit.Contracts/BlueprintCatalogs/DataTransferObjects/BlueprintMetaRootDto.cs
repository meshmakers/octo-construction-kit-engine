using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
///     Represents the content of the blueprint metadata file
/// </summary>
public class BlueprintMetaRootDto : BlueprintPropertiesDto
{
    /// <summary>
    ///     The URI of the schema for the blueprint meta.
    /// </summary>
    public const string BlueprintMetaSchemaUri = "https://schemas.meshmakers.cloud/blueprint-meta.schema.json";

    /// <summary>
    ///     Creates a new instance of the <see cref="BlueprintMetaRootDto" /> class.
    /// </summary>
    public BlueprintMetaRootDto()
    {
        CkModelDependencies = [];
    }

    /// <summary>
    ///     The URI of the schema for the blueprint meta used for serialization.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = BlueprintMetaSchemaUri;

    /// <summary>
    ///     Gets or sets the CK model dependencies of the blueprint.
    /// </summary>
    public List<CkModelIdVersionRange>? CkModelDependencies { get; set; }

    /// <summary>
    ///     Gets or sets the path to the seed data file (relative to blueprint root).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? SeedDataPath { get; set; }

    /// <summary>
    ///     Gets or sets the migration scripts from previous versions.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<BlueprintMigrationReferenceDto>? Migrations { get; set; }
}
