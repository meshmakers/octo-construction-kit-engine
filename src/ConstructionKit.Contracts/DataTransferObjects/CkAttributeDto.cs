using System.Diagnostics;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

[DebuggerDisplay("{" + nameof(AttributeId) + "}")]
public class CkAttributeDto
{
    [JsonPropertyName("id")]
    [YamlMember(Alias = "id")]
    [JsonRequired]
    public CkAttributeId AttributeId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AttributeValueTypesDto ValueType { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<object>? DefaultValues { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<CkSelectionValueDto>? SelectionValues { get; set; }
}