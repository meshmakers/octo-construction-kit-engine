using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

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
    /// Defines the record of the attribute if the value type is a record.
    /// </summary>
    [JsonConverter(typeof(CkIdRecordIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkRecordId>? ValueCkRecordId { get; set; }
    
    /// <summary>
    /// Defines the enum of the attribute if the value type is a enum.
    /// </summary>
    [JsonConverter(typeof(CkIdEnumIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkEnumId>? ValueCkEnumId { get; set; }

    /// <summary>
    /// Default value of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<object>? DefaultValues { get; set; }
    
    /// <summary>
    /// An optional description of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional meta data of the attribute 
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; set; }
    
    /// <summary>
    /// Optional flag that tells if an attribute is a data stream.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool? IsDataStream { get; set; }
}