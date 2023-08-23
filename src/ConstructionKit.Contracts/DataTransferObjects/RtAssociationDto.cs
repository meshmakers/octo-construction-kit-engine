using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

public class RtAssociationDto
{
    [JsonPropertyName("roleId")]
    [JsonRequired]
    public string RoleId { get; set; } = null!;

    [JsonPropertyName("targetRtId")]
    [JsonRequired]
    public OctoObjectId TargetRtId { get; set; }
    
    [JsonPropertyName("targetCkTypeId")] 
    [JsonRequired]
    public CkId<CkTypeId> TargetCkTypeId { get; set; }
}
