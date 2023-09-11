using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Defines enum values for an enum type.
/// </summary>
public class CkEnumValueDto
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
    
    /// <summary>
    /// A optional description of the enum value
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}
