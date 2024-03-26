using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable UnusedMember.Global

namespace Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;

/// <summary>
///     Defines an association between two entities
/// </summary>
public class RtAssociationDto : RtTypeWithAttributesDto
{
    /// <summary>
    ///     Gets or sets the role id of the association.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(CkIdAssociationRoleIdConverter))]
    public CkId<CkAssociationRoleId> RoleId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the target rt id.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(OctoObjectIdConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonOctoObjectIdConverter))]
    public OctoObjectId TargetRtId { get; set; }

    /// <summary>
    ///     Gets or sets the target ck type id.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkTypeId> TargetCkTypeId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets a list of attributes of the target ck type id, that are referential integrity attributes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkId<CkAttributeId>>? TargetCkAttributeIds { get; set; }
}