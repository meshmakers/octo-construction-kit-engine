using System.Diagnostics;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;

/// <summary>
/// Represents a role of an association between two CkEntityTypes
/// </summary>
[DebuggerDisplay("{" + nameof(AssociationRoleId) + "}")]
public class CkAssociationRoleDto
{
    /// <summary>
    /// The id of the association role
    /// </summary>
    [YamlMember(Alias = "id")]
    [JsonPropertyName("id")]
    [JsonRequired]
    public CkAssociationRoleId AssociationRoleId { get; set; }

    /// <summary>
    ///     Name of the association for inbound references (e. g. Children)
    /// </summary>
    [JsonRequired]
    public string InboundName { get; set; } = null!;

    /// <summary>
    ///     Name of the association for outbound references (e. g. Parent)
    /// </summary>
    [JsonRequired]
    public string OutboundName { get; set; } = null!;
    
    /// <summary>
    ///     Multiplicity of the inbound association
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonRequired]
    public MultiplicitiesDto InboundMultiplicity { get; set; }

    /// <summary>
    ///     Multiplicity of the outbound association
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonRequired]
    public MultiplicitiesDto OutboundMultiplicity { get; set; }
}