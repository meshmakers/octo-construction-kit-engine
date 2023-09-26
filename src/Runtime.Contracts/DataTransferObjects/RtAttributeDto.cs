using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
/// Defines an attribute of an entity
/// </summary>
public class RtAttributeDto
{
    /// <summary>
    /// Gets or sets the id of the attribute.
    /// </summary>
    [JsonRequired]
    public CkId<CkAttributeId> Id { get; set; } 

    /// <summary>
    /// Gets or sets the value of the attribute.
    /// </summary>
    public object? Value { get; set; }
}
