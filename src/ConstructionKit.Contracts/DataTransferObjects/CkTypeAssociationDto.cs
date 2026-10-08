using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Represents an assignment of a CK type to a CK association role and the target CK type.
/// </summary>
[DebuggerDisplay("{" + nameof(CkRoleId) + "} -> {" + nameof(TargetCkTypeId) + "}")]
public class CkTypeAssociationDto
{
    /// <summary>
    ///     Gets or sets the association role id.
    /// </summary>
    [JsonPropertyName("id")]
    [YamlMember(Alias = "id")]
    [JsonRequired]
    [JsonConverter(typeof(CkIdAssociationRoleIdConverter))]
    public CkId<CkAssociationRoleId> CkRoleId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the target CK type id.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkTypeId> TargetCkTypeId { get; set; } = null!;

    /// <summary>
    ///     CK v2 (F1.1-S5): narrows the target to types that (derive from <see cref="TargetCkTypeId" /> and)
    ///     implement this interface. <see cref="TargetCkTypeId" /> stays required — use <c>${System}/Entity</c> for
    ///     "any implementor". Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(CkIdInterfaceIdConverter))]
    public CkId<CkInterfaceId>? TargetCkInterfaceId { get; set; }

    /// <summary>
    ///     Gets or sets a list of attributes of the target ck type id, that are referential integrity attributes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkId<CkAttributeId>>? TargetCkAttributeIds { get; set; }
}