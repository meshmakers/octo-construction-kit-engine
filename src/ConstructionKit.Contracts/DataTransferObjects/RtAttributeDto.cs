using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

public class RtAttributeDto
{
    [JsonPropertyName("id")]
    [JsonRequired]
    public CkId<CkAttributeId> Id { get; set; } 

    [JsonPropertyName("value")] public object? Value { get; set; }
}
