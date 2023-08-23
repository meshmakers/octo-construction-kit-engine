using System.Text.Json.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Defines index fields in the database for a ck type.
/// </summary>
public class CkIndexFieldsDto
{
    /// <summary>
    /// The weight of the index. The higher the weight, the more important the index is.
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// A list of attribute names that are indexed.
    /// </summary>
    [JsonRequired] 
    public List<string> AttributeNames { get; set; } = null!;
}
