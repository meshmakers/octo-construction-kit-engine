using System.Text.Json.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

public class CkTypeIndexDto
{
    public CkTypeIndexDto()
    {
        Fields = new List<CkIndexFieldsDto>();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IndexTypeDto IndexType { get; set; }

    public string? Language { get; set; }

    public List<CkIndexFieldsDto> Fields { get; set; }
}