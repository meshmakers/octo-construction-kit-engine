using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;

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
    public CkId<CkAssociationRoleId> RoleId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the target rt id.
    /// </summary>
    [JsonRequired]
    public OctoObjectId TargetRtId { get; set; }
    
    /// <summary>
    /// Gets or sets the target ck type id.
    /// </summary>
    [JsonRequired]
    public CkId<CkTypeId> TargetCkTypeId { get; set; }
}
