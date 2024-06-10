using YamlDotNet.Serialization;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Represents the properties of a CK model
/// </summary>
public abstract class CkModelPropertiesDto
{
    /// <summary>
    ///     Gets or sets the model id.
    /// </summary>
    [JsonRequired]
    public CkModelId ModelId { get; set; } = null!;
        
    /// <summary>
    ///     An optional description of the model
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}