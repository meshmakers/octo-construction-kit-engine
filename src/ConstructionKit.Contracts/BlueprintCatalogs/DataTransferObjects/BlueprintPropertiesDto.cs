using System.Diagnostics;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
/// Represents the properties of a blueprint
/// </summary>
[DebuggerDisplay("{" + nameof(BlueprintId) + "}")]
public class BlueprintPropertiesDto
{
    /// <summary>
    ///     Gets or sets the blueprint id.
    /// </summary>
    [JsonRequired]
    public BlueprintId BlueprintId { get; set; } = null!;

    /// <summary>
    ///     An optional description of the blueprint
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}
