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
    ///     Gets or sets the blueprint dependencies. When this blueprint is
    ///     installed, the listed blueprints are resolved transitively, topo-sorted
    ///     and installed first as separate installations (each marked as a
    ///     dependency on the runtime installation record).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<BlueprintIdVersionRange>? BlueprintDependencies { get; set; }

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

    /// <summary>
    ///     Gets or sets the optional preconditions evaluated before this blueprint is applied.
    ///     Each key is the name of a blueprint variable (e.g. <c>octo.environment</c>,
    ///     <c>octo.isSystemTenant</c>) and the value is the list of acceptable values. The
    ///     blueprint is skipped when the resolved variable is missing from the tenant context
    ///     or does not match any of the listed values. YAML accepts both scalar and sequence
    ///     shapes per value — <see cref="Serialization.RequiresMapConverter"/> normalises
    ///     scalars to a single-element list during deserialisation.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public RequiresMap? Requires { get; set; }
}
