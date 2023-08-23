using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

public class RtEntityDto
{
    public RtEntityDto()
    {
        Attributes = new List<RtAttributeDto>();
        Associations = new List<RtAssociationDto>();
    }

    [JsonPropertyName("rtId")]
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

    [JsonPropertyName("ckTypeId")]
    [JsonRequired]
    public CkId<CkTypeId> CkTypeId { get; set; } 

    [JsonPropertyName("rtWellKnownName")] 
    public string? RtWellKnownName { get; set; }

    [JsonPropertyName("attributes")] 
    public List<RtAttributeDto> Attributes { get; }

    [JsonPropertyName("associations")] 
    public List<RtAssociationDto>? Associations { get; }
}
