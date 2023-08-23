using System.Text.Json.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Defines an entity
/// </summary>
public class RtEntityDto
{
    /// <summary>
    /// Creates a new instance of <see cref="RtEntityDto"/>
    /// </summary>
    public RtEntityDto()
    {
        Attributes = new List<RtAttributeDto>();
        Associations = new List<RtAssociationDto>();
    }

    /// <summary>
    /// Gets or sets the id of the entity
    /// </summary>
    [JsonRequired]
    public OctoObjectId RtId { get; set; }

    /// <summary>
    ///     Returns the creation date time
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]

    public DateTime? RtCreationDateTime { get; set; }

    /// <summary>
    ///     Returns the last change date time
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTime? RtChangedDateTime { get; set; }

    /// <summary>
    /// Gets or sets the ck type id of the entity
    /// </summary>
    [JsonRequired]
    public CkId<CkTypeId> CkTypeId { get; set; } 

    /// <summary>
    /// Gets or sets the well known name of the entity
    /// </summary>
    public string? RtWellKnownName { get; set; }

    /// <summary>
    /// Gets or sets the attributes of the entity
    /// </summary>
    public List<RtAttributeDto> Attributes { get; }

    /// <summary>
    /// Gets or sets the associations of the entity
    /// </summary>
    public List<RtAssociationDto>? Associations { get; }
}
