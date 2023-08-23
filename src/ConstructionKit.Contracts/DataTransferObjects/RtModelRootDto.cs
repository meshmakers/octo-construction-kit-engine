using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Defines an entity in the runtime model
/// </summary>
public class RtModelRootDto
{
    /// <summary>
    /// Creates a new instance of <see cref="RtModelRootDto"/>.
    /// </summary>
    public RtModelRootDto()
    {
        RtEntities = new List<RtEntityDto>();
    }

    /// <summary>
    /// Gets a list of entities in the runtime model.
    /// </summary>
    [JsonPropertyName("entities")] 
    public List<RtEntityDto> RtEntities { get; }
}
