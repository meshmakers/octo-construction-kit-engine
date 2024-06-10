using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Represents an association role in the dependency graph
/// </summary>
[DebuggerDisplay("{" + nameof(CkRoleId) + "}")]
public class CkAssociationRoleGraph : CkTypeWithAttributesGraph
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkAssociationRoleGraph" />.
    /// </summary>
    /// <param name="ckAssociationCkRoleId"></param>
    /// <param name="associationRoleDto"></param>
    public CkAssociationRoleGraph(CkId<CkAssociationRoleId> ckAssociationCkRoleId, CkAssociationRoleDto associationRoleDto)
        : base(associationRoleDto)
    {
        CkRoleId = ckAssociationCkRoleId;
        InboundName = associationRoleDto.InboundName;
        OutboundName = associationRoleDto.OutboundName;
        InboundMultiplicity = associationRoleDto.InboundMultiplicity;
        OutboundMultiplicity = associationRoleDto.OutboundMultiplicity;
        Description = associationRoleDto.Description;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkAssociationRoleGraph" />.
    /// </summary>
    /// <param name="ckRoleId"></param>
    /// <param name="inboundName"></param>
    /// <param name="outboundName"></param>
    /// <param name="inboundMultiplicity"></param>
    /// <param name="outboundMultiplicity"></param>
    /// <param name="definedAttributes"></param>
    /// <param name="allAttributes"></param>
    /// <param name="description"></param>
    [JsonConstructor]
    public CkAssociationRoleGraph(CkId<CkAssociationRoleId> ckRoleId, string inboundName, string outboundName,
        MultiplicitiesDto inboundMultiplicity, MultiplicitiesDto outboundMultiplicity,
        IReadOnlyCollection<CkTypeAttributeDto> definedAttributes,
        IReadOnlyDictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> allAttributes, string description)
        : base(definedAttributes, allAttributes)
    {
        CkRoleId = ckRoleId;
        InboundName = inboundName;
        OutboundName = outboundName;
        InboundMultiplicity = inboundMultiplicity;
        OutboundMultiplicity = outboundMultiplicity;
        Description = description;
    }

    /// <summary>
    ///     Returns the ck association id of the association role.
    /// </summary>
    public CkId<CkAssociationRoleId> CkRoleId { get; }

    /// <summary>
    ///     Name of the association for inbound references (e. g. Children)
    /// </summary>
    public string InboundName { get; }

    /// <summary>
    ///     Name of the association for outbound references (e. g. Parent)
    /// </summary>
    public string OutboundName { get; }

    /// <summary>
    ///     Multiplicity of the inbound association
    /// </summary>
    public MultiplicitiesDto InboundMultiplicity { get; }

    /// <summary>
    ///     Multiplicity of the outbound association
    /// </summary>
    public MultiplicitiesDto OutboundMultiplicity { get; }
    
    /// <summary>
    ///     An optional description of the association role
    /// </summary>
    public string? Description { get; set; }
}