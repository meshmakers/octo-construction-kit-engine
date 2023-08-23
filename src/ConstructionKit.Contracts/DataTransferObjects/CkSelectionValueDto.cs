using System.Text.Json.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

public class CkSelectionValueDto
{
    [JsonRequired] 
    public int Key { get; set; }

    [JsonRequired] 
    public string Name { get; set; } = null!;
}
