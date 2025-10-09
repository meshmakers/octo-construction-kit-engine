using System.Diagnostics;
using YamlDotNet.Serialization;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Represents the properties of a CK model
/// </summary>
[DebuggerDisplay("{" + nameof(ModelId) + "}")]
public class CkModelPropertiesDto
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