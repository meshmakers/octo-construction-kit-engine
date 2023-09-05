using System.Diagnostics;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Represents an attribute
/// </summary>
[DebuggerDisplay("{" + nameof(AttributeId) + "}")]
public class CkAttributeDto
{
    /// <summary>
    /// The id of the attribute
    /// </summary> 
    [JsonPropertyName("id")]
    [YamlMember(Alias = "id")]
    [JsonRequired]
    public CkAttributeId AttributeId { get; set; }

    /// <summary>
    /// Value type of the attribute
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AttributeValueTypesDto ValueType { get; set; }

    /// <summary>
    /// Default value of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<object>? DefaultValues { get; set; }

    /// <summary>
    /// Selection values of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<CkSelectionValueDto>? SelectionValues { get; set; }
    
    /// <summary>
    /// If true, the attribute is optional, that means it can be null
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsOptional { get; set; }
}