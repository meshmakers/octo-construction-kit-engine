using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
/// Defines an association between two entities
/// </summary>
public class RtAssociationDto
{
    /// <summary>
    /// Gets or sets the role id of the association.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(CkIdAssociationIdConverter))]
    public CkId<CkAssociationRoleId> RoleId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the target rt id.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(OctoObjectIdConverter))]
    public OctoObjectId TargetRtId { get; set; }
    
    /// <summary>
    /// Gets or sets the target ck type id.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkTypeId> TargetCkTypeId { get; set; }
}
