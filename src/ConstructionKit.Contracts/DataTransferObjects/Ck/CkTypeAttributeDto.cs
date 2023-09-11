using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Defines an assignment of a CK type to a CK attribute.
/// </summary>
[DebuggerDisplay("{" + nameof(CkAttributeId) + "} -> {" + nameof(AttributeName) + "}")]
public class CkTypeAttributeDto
{
    /// <summary>
    /// Gets or sets the CK attribute id.
    /// </summary>
    [YamlMember(Alias = "id")]
    [JsonPropertyName("id")]
    [JsonRequired]
    [JsonConverter(typeof(CkIdAttributeIdConverter))]
    public CkId<CkAttributeId> CkAttributeId { get; set; }

    /// <summary>
    /// Gets or sets the name of the attribute.
    /// </summary>
    [YamlMember(Alias = "name")]
    [JsonPropertyName("name")] 
    [JsonRequired]
    public string AttributeName { get; set; } = null!;

    /// <summary>
    /// Gets or sets a list of values that are used for auto completion.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<object>? AutoCompleteValues { get; set; }
}