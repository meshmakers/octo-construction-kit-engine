using System.Text.Json.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

public class CkIndexFieldsDto
{
    public int? Weight { get; set; }

    [JsonRequired] 
    public List<string> AttributeNames { get; set; } = null!;
}
