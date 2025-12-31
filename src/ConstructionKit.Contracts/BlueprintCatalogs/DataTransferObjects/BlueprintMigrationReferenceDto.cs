using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
///     Reference to a migration script within a blueprint
/// </summary>
public class BlueprintMigrationReferenceDto
{
    /// <summary>
    ///     The source version this migration applies from
    /// </summary>
    public required string FromVersion { get; set; }

    /// <summary>
    ///     Path to the migration script file (relative to blueprint root)
    /// </summary>
    public required string ScriptPath { get; set; }

    /// <summary>
    ///     Optional description of the migration
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}
