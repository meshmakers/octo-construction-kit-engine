using System.Text.Json.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Defines selection values for a ck attribute.
/// </summary>
public class CkSelectionValueDto
{
    /// <summary>
    /// Key of the selection value.
    /// </summary>
    public int Key { get; set; }

    /// <summary>
    /// Display name of the selection value.
    /// </summary>
    [JsonRequired] 
    public string Name { get; set; } = null!;
}
